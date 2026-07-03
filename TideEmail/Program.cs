// Daily tide report emailer for 136th Street, Ocean City, MD.
// Uses NOAA Tides and Currents API (station 8570283 — Ocean City Inlet).
// Weather from Open-Meteo (no API key required).

using System.Globalization;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Sessions;
using Anthropic.Models.Beta.Sessions.Events;
using DailyTidesWeather.Shared;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace TideEmail;

internal sealed record TidePrediction(DateTime Time, string Type, double Height);

internal sealed record WeatherHour(DateTime Hour, double Temp, double WindSpeed, double WindDir, double Uv);

internal static class Program
{
    private const string NoaaStation = "8570283"; // Ocean City (Inlet), MD
    private static readonly (int Month, int Day) SeasonStart = (5, 17);  // May 17
    private static readonly (int Month, int Day) SeasonEnd   = (10, 15); // October 15
    private const double OcLat = 38.3365;
    private const double OcLon = -75.0849;

    // Source URLs — used for clickable links in the HTML email
    private const string Lk = "target='_blank' rel='noopener noreferrer'"; // shared link attributes
    private const string UrlNoaaTides   = "https://tidesandcurrents.noaa.gov/noaatidepredictions.html?id=8570283";
    private const string UrlNoaaWater   = "https://tidesandcurrents.noaa.gov/physocean.html?id=8570283";
    private const string UrlNoaaStation = "https://tidesandcurrents.noaa.gov/stationhome.html?id=8570283";
    private const string UrlEpaUv       = "https://www.epa.gov/sunsafety/uv-index-scale-0";
    private const string UrlOpenMeteo   = "https://open-meteo.com/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static async Task Main()
    {
        // On local runs, persist any set secrets and hydrate any missing ones
        // from the per-user store; no-op in GitHub Actions.
        LocalSecrets.Sync();

        var today = DateOnly.FromDateTime(DateTime.Now);

        if (!InSeason(today))
        {
            Console.WriteLine($"Outside season ({today:yyyy-MM-dd}). No email sent.");
            return;
        }

        var gmailAddress  = RequireEnv("GMAIL_ADDRESS");
        var gmailPassword = RequireEnv("GMAIL_APP_PASSWORD");
        var recipient     = RequireEnv("RECIPIENT_EMAIL");

        Console.WriteLine($"Fetching NOAA tides for {today:yyyy-MM-dd} …");
        var tides = await FetchTides(today);

        Console.WriteLine("Fetching NOAA water temperature …");
        var (waterTemp, readingCount) = await FetchWaterTemp(today);
        Console.WriteLine(waterTemp is not null
            ? $"  Water temp: {waterTemp.Value.ToString("0.0", Inv)}°F (avg of {readingCount} lowest readings, last 24 hr)"
            : "  Water temp unavailable, continuing without it.");

        Console.WriteLine("Fetching Open-Meteo weather forecast …");
        var weather = await FetchWeather(today);

        Console.WriteLine("Generating tide narrative via Claude …");
        var narrative = await GenerateNarrative(today, tides, weather, waterTemp);

        var subject = $"Tide Report — OC 136th St — {ToDate(today).ToString("ddd MMM d", Inv)}";
        var html    = BuildHtml(today, tides, narrative, weather, waterTemp, readingCount);
        var text    = BuildText(today, tides, narrative, weather, waterTemp, readingCount);

        Console.WriteLine($"Sending email to {recipient} …");
        await SendEmail(subject, html, text, gmailAddress, gmailPassword, recipient);
        Console.WriteLine("Done.");
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Missing required environment variable: {name}. "
            + $"Set it in the environment or add it to {LocalSecrets.SettingsPath}.");

    private static DateTime ToDate(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    private static bool InSeason(DateOnly today)
    {
        var start = new DateOnly(today.Year, SeasonStart.Month, SeasonStart.Day);
        var end   = new DateOnly(today.Year, SeasonEnd.Month, SeasonEnd.Day);
        return start <= today && today <= end;
    }

    private static async Task<List<TidePrediction>> FetchTides(DateOnly today)
    {
        var dateStr = today.ToString("yyyyMMdd", Inv);
        var url = "https://api.tidesandcurrents.noaa.gov/api/prod/datagetter"
                  + $"?begin_date={dateStr}&end_date={dateStr}&station={NoaaStation}"
                  + "&product=predictions&datum=MLLW&time_zone=lst_ldt&interval=hilo"
                  + "&units=english&application=tide_email&format=json";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"NOAA API error: {error.GetProperty("message").GetString()}");

        var tides = new List<TidePrediction>();
        foreach (var entry in doc.RootElement.GetProperty("predictions").EnumerateArray())
        {
            tides.Add(new TidePrediction(
                DateTime.ParseExact(entry.GetProperty("t").GetString()!, "yyyy-MM-dd HH:mm", Inv),
                entry.GetProperty("type").GetString()!,
                double.Parse(entry.GetProperty("v").GetString()!, Inv)));
        }
        return tides;
    }

    /// <summary>
    /// Returns (avg temp °F, reading count) from the previous 24 hours.
    /// Fetches yesterday + today, filters to readings within the last 24 hours,
    /// then averages the 20 lowest readings (all of them when fewer than 20).
    /// Returns (null, 0) on failure or no data.
    /// </summary>
    private static async Task<(double? AvgTempF, int ReadingCount)> FetchWaterTemp(DateOnly today)
    {
        var yesterday = today.AddDays(-1);
        var url = "https://api.tidesandcurrents.noaa.gov/api/prod/datagetter"
                  + $"?begin_date={yesterday.ToString("yyyyMMdd", Inv)}&end_date={today.ToString("yyyyMMdd", Inv)}"
                  + $"&station={NoaaStation}&product=water_temperature&time_zone=lst_ldt"
                  + "&units=english&format=json";
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("data", out var readings))
                return (null, 0);

            var cutoff = DateTime.Now - TimeSpan.FromHours(24);
            var values = new List<double>();
            foreach (var r in readings.EnumerateArray())
            {
                if (DateTime.TryParseExact(r.GetProperty("t").GetString(), "yyyy-MM-dd HH:mm", Inv,
                        DateTimeStyles.None, out var t)
                    && t >= cutoff
                    && double.TryParse(r.GetProperty("v").GetString(), NumberStyles.Float, Inv, out var v))
                {
                    values.Add(v);
                }
            }
            if (values.Count > 0)
            {
                var lowest = values.OrderBy(v => v).Take(20).ToList();
                return (lowest.Average(), lowest.Count);
            }
        }
        catch
        {
            // Water temp is optional — continue without it on any failure.
        }
        return (null, 0);
    }

    private static string WindDirLabel(double deg)
    {
        string[] dirs = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                         "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];
        return dirs[(int)Math.Round(deg / 22.5, MidpointRounding.ToEven) % 16];
    }

    private static async Task<List<WeatherHour>> FetchWeather(DateOnly today)
    {
        // Open-Meteo rejects %2C-encoded commas, so the hourly list is kept literal
        var dateStr = today.ToString("yyyy-MM-dd", Inv);
        var url =
            "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={OcLat.ToString(Inv)}&longitude={OcLon.ToString(Inv)}"
            + "&hourly=temperature_2m,windspeed_10m,winddirection_10m,uv_index"
            + "&temperature_unit=fahrenheit&windspeed_unit=mph"
            + "&timezone=America%2FNew_York"
            + $"&start_date={dateStr}&end_date={dateStr}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var hourly     = doc.RootElement.GetProperty("hourly");
        var times      = hourly.GetProperty("time");
        var temps      = hourly.GetProperty("temperature_2m");
        var windSpeeds = hourly.GetProperty("windspeed_10m");
        var windDirs   = hourly.GetProperty("winddirection_10m");
        var uvs        = hourly.GetProperty("uv_index");

        var rows = new List<WeatherHour>();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            var dt = DateTime.Parse(times[i].GetString()!, Inv);
            if (dt.Hour is >= 7 and <= 19)
            {
                rows.Add(new WeatherHour(
                    dt,
                    temps[i].GetDouble(),
                    windSpeeds[i].GetDouble(),
                    windDirs[i].GetDouble(),
                    uvs[i].GetDouble()));
            }
        }
        return rows;
    }

    private static string UvLabel(double uv) => uv switch
    {
        < 3  => "Low",
        < 6  => "Moderate",
        < 8  => "High",
        < 11 => "Very High",
        _    => "Extreme",
    };

    private static string UvColor(double uv) => uv switch
    {
        < 3  => "#2d8a2d",
        < 6  => "#b8860b",
        < 8  => "#d4600a",
        < 11 => "#c0392b",
        _    => "#8e44ad",
    };

    private static string FmtHeight(double height) => height.ToString("+0.00;-0.00", Inv) + " ft";

    private static IEnumerable<string> TideSummaryLines(List<TidePrediction> tides) =>
        tides.Select(entry =>
        {
            var label = entry.Type == "H" ? "High" : "Low";
            return $"  {label} tide at {entry.Time.ToString("h:mm tt", Inv)}: {FmtHeight(entry.Height)}";
        });

    private static IEnumerable<string> WeatherSummaryLines(List<WeatherHour> weather) =>
        weather.Select(w =>
            $"  {w.Hour.ToString("h tt", Inv)}: {w.Temp.ToString("0", Inv)}°F, "
            + $"Wind {WindDirLabel(w.WindDir)} {w.WindSpeed.ToString("0", Inv)} mph, "
            + $"UV {w.Uv.ToString("0.0", Inv)} ({UvLabel(w.Uv)})");

    private static async Task<string> GenerateNarrative(DateOnly today, List<TidePrediction> tides,
                                                        List<WeatherHour> weather, double? waterTemp)
    {
        var waterLine = waterTemp is not null
            ? $"\nOcean water temperature (avg of the lowest readings over the last 24 hours): {waterTemp.Value.ToString("0.0", Inv)}°F"
            : "";
        var message =
            $"Today is {ToDate(today).ToString("dddd, MMMM d, yyyy", Inv)}. "
            + "Here are today's tide predictions:\n" + string.Join("\n", TideSummaryLines(tides))
            + waterLine
            + "\n\nWeather forecast for Ocean City, MD:\n" + string.Join("\n", WeatherSummaryLines(weather));

        var client = new AnthropicClient(); // reads ANTHROPIC_API_KEY from env
        var session = await client.Beta.Sessions.Create(new SessionCreateParams
        {
            Agent = RequireEnv("TIDE_AGENT_ID"),
            EnvironmentID = RequireEnv("TIDE_ENV_ID"),
        });

        var parts = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        // Open the event stream before sending so no early events are missed.
        var streamTask = Task.Run(async () =>
        {
            await foreach (var ev in client.Beta.Sessions.Events.StreamStreaming(
                               session.ID, new EventStreamParams(), cts.Token))
            {
                if (ev.TryPickAgentMessageEvent(out var msg))
                {
                    foreach (var block in msg.Content)
                        parts.Append(block.Text);
                }
                if (ev.TryPickSessionStatusTerminatedEvent(out _) || ev.TryPickSessionStatusIdleEvent(out _))
                    break;
            }
        }, cts.Token);

        // Give the SSE connection a moment to establish, then kick off the agent.
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
        await client.Beta.Sessions.Events.Send(session.ID, new EventSendParams
        {
            Events =
            [
                new BetaManagedAgentsUserMessageEventParams
                {
                    Type = BetaManagedAgentsUserMessageEventParamsType.UserMessage,
                    Content =
                    [
                        new BetaManagedAgentsTextBlock
                        {
                            Type = BetaManagedAgentsTextBlockType.Text,
                            Text = message,
                        },
                    ],
                },
            ],
        });

        await streamTask;
        return parts.ToString().Trim();
    }

    private static string TideRow(TidePrediction entry)
    {
        var label   = entry.Type == "H" ? "High Tide" : "Low Tide ";
        var time12  = entry.Time.ToString("h:mm tt", Inv);
        var tideBar = new string('█', Math.Max(0, (int)((entry.Height + 1) * 6)));
        return $"  {label}  |  {time12,8}  |  {FmtHeight(entry.Height)}  |  {tideBar}";
    }

    private static string WaterTempHtml(double? waterTemp, int readingCount)
    {
        if (waterTemp is null)
            return "";
        var note = " <span style='color:#aaa;font-size:11px;'>"
                   + $"(avg of {readingCount} lowest readings in 24 hrs, Ocean City Inlet)</span>";
        return "<div style='margin:0 28px 16px;padding:12px 16px;"
               + "background:#e8f4fd;border-left:4px solid #0a3d6b;border-radius:4px;"
               + "font-size:15px;color:#333;'>"
               + $"<a href='{UrlNoaaWater}' {Lk} style='font-weight:bold;color:#333;text-decoration:none;'>"
               + "🌊 Water Temp:</a> "
               + $"<span style='font-size:18px;font-weight:bold;color:#0a3d6b;'>{waterTemp.Value.ToString("0.0", Inv)}°F</span>"
               + $"{note}</div>";
    }

    private static string BuildHtml(DateOnly today, List<TidePrediction> tides, string narrative,
                                    List<WeatherHour> weather, double? waterTemp, int readingCount)
    {
        var dateLabel = ToDate(today).ToString("dddd, MMMM d, yyyy", Inv);

        var tideRows = new StringBuilder();
        foreach (var entry in tides)
        {
            var label  = entry.Type == "H" ? "High Tide" : "Low Tide";
            var time12 = entry.Time.ToString("h:mm tt", Inv);
            var color  = entry.Type == "H" ? "#1a6b3c" : "#2c5f8a";
            tideRows.Append(
                "\n        <tr>"
                + $"<td style='padding:10px 16px;font-weight:bold;color:{color};'>{label}</td>"
                + $"<td style='padding:10px 16px;'>{time12}</td>"
                + $"<td style='padding:10px 16px;text-align:right;'>{FmtHeight(entry.Height)}</td>"
                + "</tr>");
        }

        var weatherRows = new StringBuilder();
        foreach (var w in weather)
        {
            var wcolor = UvColor(w.Uv);
            weatherRows.Append(
                "\n        <tr>"
                + $"<td style='padding:8px 16px;'>{w.Hour.ToString("h tt", Inv)}</td>"
                + $"<td style='padding:8px 16px;text-align:right;'>{w.Temp.ToString("0", Inv)}°F</td>"
                + $"<td style='padding:8px 16px;'>{WindDirLabel(w.WindDir)} {w.WindSpeed.ToString("0", Inv)} mph</td>"
                + $"<td style='padding:8px 16px;text-align:right;font-weight:bold;color:{wcolor};'>"
                + $"{w.Uv.ToString("0.0", Inv)} <span style='font-weight:normal;font-size:12px;'>({UvLabel(w.Uv)})</span>"
                + "</td></tr>");
        }

        return $$"""
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"></head>
<body style="font-family:Georgia,serif;background:#f0f6ff;margin:0;padding:24px;">
  <div style="max-width:560px;margin:0 auto;background:#fff;border-radius:10px;
              box-shadow:0 2px 8px rgba(0,0,0,.12);overflow:hidden;">

    <div style="background:#0a3d6b;color:#fff;padding:24px 28px;">
      <div style="font-size:22px;font-weight:bold;">🌊 Daily Tide Report</div>
      <div style="font-size:14px;margin-top:4px;opacity:.85;">136th Street — Ocean City, MD</div>
      <div style="font-size:13px;margin-top:2px;opacity:.70;">{{dateLabel}}</div>
    </div>

    <div style="padding:20px 28px 16px;font-size:15px;line-height:1.65;color:#333;">
      {{narrative}}
    </div>

    {{WaterTempHtml(waterTemp, readingCount)}}

    <div style="padding:0 28px 20px;">
      <div style="font-size:12px;font-weight:bold;color:#555;margin-bottom:6px;
                  text-transform:uppercase;letter-spacing:.05em;">
        <a href='{{UrlNoaaTides}}' {{Lk}} style="color:#555;text-decoration:none;">Tides</a>
      </div>
      <table style="width:100%;border-collapse:collapse;">
        <thead>
          <tr style="border-bottom:2px solid #d0dde8;">
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Tide</th>
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Time (ET)</th>
            <th style="padding:8px 16px;text-align:right;color:#555;font-size:13px;">Height</th>
          </tr>
        </thead>
        <tbody style="font-size:15px;">{{tideRows}}
        </tbody>
      </table>
    </div>

    <div style="padding:0 28px 20px;">
      <div style="font-size:12px;font-weight:bold;color:#555;margin-bottom:6px;
                  text-transform:uppercase;letter-spacing:.05em;">
        <a href='{{UrlOpenMeteo}}' {{Lk}} style="color:#555;text-decoration:none;">Hourly Weather</a>
      </div>
      <table style="width:100%;border-collapse:collapse;">
        <thead>
          <tr style="border-bottom:2px solid #d0dde8;">
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Time</th>
            <th style="padding:8px 16px;text-align:right;color:#555;font-size:13px;">Temp</th>
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Wind</th>
            <th style="padding:8px 16px;text-align:right;color:#555;font-size:13px;">
              <a href='{{UrlEpaUv}}' {{Lk}} style="color:#555;text-decoration:none;">UV Index</a>
            </th>
          </tr>
        </thead>
        <tbody style="font-size:14px;">{{weatherRows}}
        </tbody>
      </table>
    </div>

    <div style="padding:12px 28px 20px;font-size:12px;color:#888;">
      Tide predictions and water temperature from
      <a href='{{UrlNoaaStation}}' {{Lk}} style="color:#888;">NOAA Tides &amp; Currents,
      Station 8570283</a> (Ocean City Inlet, MD). Heights above MLLW. Times in Eastern Time.<br>
      Weather forecast from <a href='{{UrlOpenMeteo}}' {{Lk}} style="color:#888;">Open-Meteo</a>.
      UV scale from <a href='{{UrlEpaUv}}' {{Lk}} style="color:#888;">EPA</a>.
    </div>
  </div>
</body>
</html>
""";
    }

    private static string BuildText(DateOnly today, List<TidePrediction> tides, string narrative,
                                    List<WeatherHour> weather, double? waterTemp, int readingCount)
    {
        var dateLabel = ToDate(today).ToString("dddd, MMMM d, yyyy", Inv);
        var lines = new List<string>
        {
            "Daily Tide Report — 136th Street, Ocean City, MD",
            dateLabel,
            "",
            narrative,
            "",
        };
        if (waterTemp is not null)
        {
            lines.Add($"Water Temp: {waterTemp.Value.ToString("0.0", Inv)}°F (avg of {readingCount} lowest readings in 24 hrs, Ocean City Inlet)");
            lines.Add("");
        }
        lines.AddRange(
        [
            "TIDES",
            $"  {"Tide",-11}  {"Time (ET)",-10}  Height",
            "  " + new string('-', 40),
        ]);
        lines.AddRange(tides.Select(TideRow));
        lines.AddRange(
        [
            "",
            "HOURLY WEATHER",
            $"  {"Time",-7}  {"Temp",6}  {"Wind",-16}  UV Index",
            "  " + new string('-', 50),
        ]);
        foreach (var w in weather)
        {
            var wind = $"{WindDirLabel(w.WindDir)} {w.WindSpeed.ToString("0", Inv)} mph";
            lines.Add(
                $"  {w.Hour.ToString("h tt", Inv),-7}  {w.Temp.ToString("0", Inv)}°F    {wind,-16}  "
                + $"{w.Uv.ToString("0.0", Inv)} ({UvLabel(w.Uv)})");
        }
        lines.AddRange(
        [
            "",
            "Source: NOAA Tides & Currents, Station 8570283 (Ocean City Inlet, MD)",
            "Heights above MLLW. Times in Eastern Time.",
            "Weather: Open-Meteo (open-meteo.com)",
        ]);
        return string.Join("\n", lines);
    }

    private static List<string> ParseRecipients(string raw) =>
        raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .ToList();

    private static async Task SendEmail(string subject, string html, string text,
                                        string sender, string password, string recipient)
    {
        var recipients = ParseRecipients(recipient);

        var msg = new MimeMessage();
        msg.Subject = subject;
        msg.From.Add(MailboxAddress.Parse(sender));
        msg.To.Add(MailboxAddress.Parse(sender)); // header only — actual delivery below
        msg.Body = new BodyBuilder { TextBody = text, HtmlBody = html }.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(sender, password);
        // Envelope recipients are the parsed list (BCC-style), matching the Python smtplib behavior.
        await smtp.SendAsync(msg, MailboxAddress.Parse(sender), recipients.Select(MailboxAddress.Parse));
        await smtp.DisconnectAsync(true);
    }
}
