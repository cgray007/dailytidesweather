using System.Text;
using DanaPointEmail.Helpers;
using DanaPointEmail.Models;

namespace DanaPointEmail.Services;

/// <summary>Builds the HTML and plain-text bodies of the Dana Point tide report email.</summary>
internal static class EmailBuilder
{
    private const string Lk = "target='_blank' rel='noopener noreferrer'";
    private const string UrlNoaaTides   = "https://tidesandcurrents.noaa.gov/noaatidepredictions.html?id=9410580";
    private const string UrlNoaaWater   = "https://tidesandcurrents.noaa.gov/physocean.html?id=9410580";
    private const string UrlNoaaStation = "https://tidesandcurrents.noaa.gov/stationhome.html?id=9410580";
    private const string UrlEpaUv       = "https://www.epa.gov/sunsafety/uv-index-scale-0";
    private const string UrlOpenMeteo   = "https://open-meteo.com/";
    private const string UrlSunTimes    = "https://www.timeanddate.com/sun/usa/dana-point";

    internal static string BuildHtml(DateOnly today, List<TidePrediction> tides, string narrative,
                                     List<WeatherHour> weather, double? waterTemp, int readingCount,
                                     string? sunrise = null, string? sunset = null,
                                     string? moonName = null, string? moonEmoji = null, double moonIllumination = 0)
    {
        var dateLabel = Formatting.ToDate(today).ToString("dddd, MMMM d, yyyy", Formatting.Inv);

        var tideRows = new StringBuilder();
        foreach (var entry in tides)
        {
            var label  = entry.Type == "H" ? "High Tide" : "Low Tide";
            var time12 = entry.Time.ToString("h:mm tt", Formatting.Inv);
            var color  = entry.Type == "H" ? "#1a6b3c" : "#2c5f8a";
            tideRows.Append(
                "\n        <tr>"
                + $"<td style='padding:10px 16px;font-weight:bold;color:{color};'>{label}</td>"
                + $"<td style='padding:10px 16px;'>{time12}</td>"
                + $"<td style='padding:10px 16px;text-align:right;'>{Formatting.Height(entry.Height)}</td>"
                + "</tr>");
        }

        var weatherRows = new StringBuilder();
        foreach (var w in weather)
        {
            var wcolor = Formatting.UvColor(w.Uv);
            weatherRows.Append(
                "\n        <tr>"
                + $"<td style='padding:8px 16px;'>{w.Hour.ToString("h tt", Formatting.Inv)}</td>"
                + $"<td style='padding:8px 16px;text-align:right;'>{w.Temp.ToString("0", Formatting.Inv)}°F</td>"
                + $"<td style='padding:8px 16px;'>{Formatting.WindDirLabel(w.WindDir)} {w.WindSpeed.ToString("0", Formatting.Inv)} mph</td>"
                + $"<td style='padding:8px 16px;text-align:right;'>{w.PrecipChance.ToString("0", Formatting.Inv)}%</td>"
                + $"<td style='padding:8px 16px;text-align:right;'>{w.CloudCover.ToString("0", Formatting.Inv)}%</td>"
                + $"<td style='padding:8px 16px;text-align:right;font-weight:bold;color:{wcolor};'>"
                + $"{w.Uv.ToString("0.0", Formatting.Inv)} <span style='font-weight:normal;font-size:12px;'>({Formatting.UvLabel(w.Uv)})</span>"
                + "</td></tr>");
        }

        return $$"""
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"></head>
<body style="font-family:Georgia,serif;background:#f0f6ff;margin:0;padding:24px;">
  <div style="max-width:560px;margin:0 auto;background:#fff;border-radius:10px;
              box-shadow:0 2px 8px rgba(0,0,0,.12);overflow:hidden;">

    <div style="background:#1a4a2e;color:#fff;padding:24px 28px;">
      <div style="font-size:22px;font-weight:bold;">🌊 Daily Tide Report</div>
      <div style="font-size:14px;margin-top:4px;opacity:.85;">Strands Beach — Dana Point, CA</div>
      <div style="font-size:13px;margin-top:2px;opacity:.70;">{{dateLabel}}</div>
    </div>

    <div style="padding:20px 28px 16px;font-size:15px;line-height:1.65;color:#333;">
      {{narrative}}
    </div>

    {{WaterTempHtml(waterTemp, readingCount)}}
    {{SunTimesHtml(sunrise, sunset)}}
    {{MoonPhaseHtml(moonName, moonEmoji, moonIllumination)}}

    <div style="padding:0 28px 20px;">
      <div style="font-size:12px;font-weight:bold;color:#555;margin-bottom:6px;
                  text-transform:uppercase;letter-spacing:.05em;">
        <a href='{{UrlNoaaTides}}' {{Lk}} style="color:#555;text-decoration:none;">Tides</a>
      </div>
      <table style="width:100%;border-collapse:collapse;">
        <thead>
          <tr style="border-bottom:2px solid #d0dde8;">
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Tide</th>
            <th style="padding:8px 16px;text-align:left;color:#555;font-size:13px;">Time (PT)</th>
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
            <th style="padding:8px 16px;text-align:right;color:#555;font-size:13px;">Precip</th>
            <th style="padding:8px 16px;text-align:right;color:#555;font-size:13px;">Clouds</th>
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
      Station 9410580</a> (Dana Point, CA). Heights above MLLW. Times in Pacific Time.<br>
      Weather forecast from <a href='{{UrlOpenMeteo}}' {{Lk}} style="color:#888;">Open-Meteo</a>.
      UV scale from <a href='{{UrlEpaUv}}' {{Lk}} style="color:#888;">EPA</a>.
    </div>
  </div>
</body>
</html>
""";
    }

    internal static string BuildText(DateOnly today, List<TidePrediction> tides, string narrative,
                                     List<WeatherHour> weather, double? waterTemp, int readingCount,
                                     string? sunrise = null, string? sunset = null,
                                     string? moonName = null, string? moonEmoji = null, double moonIllumination = 0)
    {
        var dateLabel = Formatting.ToDate(today).ToString("dddd, MMMM d, yyyy", Formatting.Inv);
        var lines = new List<string>
        {
            "Daily Tide Report — Strands Beach, Dana Point, CA",
            dateLabel,
            "",
            narrative,
            "",
        };
        if (waterTemp is not null)
        {
            lines.Add($"Water Temp: {waterTemp.Value.ToString("0.0", Formatting.Inv)}°F (avg of {readingCount} lowest readings in 24 hrs, station 9410580)");
            lines.Add("");
        }
        if (sunrise is not null || sunset is not null)
        {
            var parts = new List<string>();
            if (sunrise is not null) parts.Add($"Sunrise: {sunrise}");
            if (sunset  is not null) parts.Add($"Sunset:  {sunset}");
            lines.Add(string.Join("   |   ", parts));
            lines.Add("");
        }
        if (moonName is not null)
        {
            lines.Add($"Moon:  {moonEmoji} {moonName} ({moonIllumination.ToString("0.0", Formatting.Inv)}% illuminated)");
            lines.Add("");
        }
        lines.AddRange(
        [
            "TIDES",
            $"  {"Tide",-11}  {"Time (PT)",-10}  Height",
            "  " + new string('-', 40),
        ]);
        lines.AddRange(tides.Select(TideRow));
        lines.AddRange(
        [
            "",
            "HOURLY WEATHER",
            $"  {"Time",-7}  {"Temp",6}  {"Wind",-16}  {"Precip",6}  {"Clouds",6}  UV Index",
            "  " + new string('-', 66),
        ]);
        foreach (var w in weather)
        {
            var wind = $"{Formatting.WindDirLabel(w.WindDir)} {w.WindSpeed.ToString("0", Formatting.Inv)} mph";
            lines.Add(
                $"  {w.Hour.ToString("h tt", Formatting.Inv),-7}  {w.Temp.ToString("0", Formatting.Inv)}°F    {wind,-16}  "
                + $"{w.PrecipChance.ToString("0", Formatting.Inv) + "%",6}  "
                + $"{w.CloudCover.ToString("0", Formatting.Inv) + "%",6}  "
                + $"{w.Uv.ToString("0.0", Formatting.Inv)} ({Formatting.UvLabel(w.Uv)})");
        }
        lines.AddRange(
        [
            "",
            "Source: NOAA Tides & Currents, Station 9410580 (Dana Point, CA)",
            "Heights above MLLW. Times in Pacific Time.",
            "Weather: Open-Meteo (open-meteo.com)",
        ]);
        return string.Join("\n", lines);
    }

    private static string TideRow(TidePrediction entry)
    {
        var label   = entry.Type == "H" ? "High Tide" : "Low Tide ";
        var time12  = entry.Time.ToString("h:mm tt", Formatting.Inv);
        var tideBar = new string('█', Math.Max(0, (int)((entry.Height + 1) * 6)));
        return $"  {label}  |  {time12,8}  |  {Formatting.Height(entry.Height)}  |  {tideBar}";
    }

    private static string WaterTempHtml(double? waterTemp, int readingCount)
    {
        if (waterTemp is null)
            return "";
        var note = " <span style='color:#aaa;font-size:11px;'>"
                   + $"(avg of {readingCount} lowest readings in 24 hrs, station 9410580)</span>";
        return "<div style='margin:0 28px 16px;padding:12px 16px;"
               + "background:#e8f4fd;border-left:4px solid #1a4a2e;border-radius:4px;"
               + "font-size:15px;color:#333;'>"
               + $"<a href='{UrlNoaaWater}' {Lk} style='font-weight:bold;color:#333;text-decoration:none;'>"
               + "🌊 Water Temp:</a> "
               + $"<span style='font-size:18px;font-weight:bold;color:#1a4a2e;'>{waterTemp.Value.ToString("0.0", Formatting.Inv)}°F</span>"
               + $"{note}</div>";
    }

    private static string SunTimesHtml(string? sunrise, string? sunset)
    {
        if (sunrise is null && sunset is null) return "";
        var parts = new List<string>();
        if (sunrise is not null)
            parts.Add("<div style='flex:1;padding:10px 16px;background:#fff8e8;"
                      + "border-left:4px solid #d4860a;border-radius:4px;font-size:14px;color:#333;'>"
                      + $"<a href='{UrlSunTimes}' {Lk} style='font-weight:bold;color:#333;text-decoration:none;'>"
                      + $"🌅 Sunrise:</a> {sunrise}</div>");
        if (sunset is not null)
            parts.Add("<div style='flex:1;padding:10px 16px;background:#f2ecf8;"
                      + "border-left:4px solid #7c4db5;border-radius:4px;font-size:14px;color:#333;'>"
                      + $"<a href='{UrlSunTimes}' {Lk} style='font-weight:bold;color:#333;text-decoration:none;'>"
                      + $"🌇 Sunset:</a> {sunset}</div>");
        return "<div style='margin:0 28px 16px;display:flex;gap:12px;'>"
               + string.Join("", parts) + "</div>";
    }

    private static string MoonPhaseHtml(string? moonName, string? moonEmoji, double moonIllumination)
    {
        if (moonName is null) return "";
        var pct = moonIllumination.ToString("0.0", Formatting.Inv);
        var barWidth = (int)Math.Round(moonIllumination);
        var bar = "<div style='margin-top:6px;height:6px;border-radius:3px;background:#ddd;'>"
                  + $"<div style='width:{barWidth}%;height:100%;border-radius:3px;background:#555;'></div></div>";
        return "<div style='margin:0 28px 16px;padding:10px 16px;"
               + "background:#f5f5f0;border-left:4px solid #555;border-radius:4px;"
               + "font-size:14px;color:#333;'>"
               + $"<span style='font-weight:bold;'>{moonEmoji} Moon Phase:</span> "
               + $"{moonName} &mdash; <span style='font-weight:bold;'>{pct}%</span> illuminated"
               + bar
               + "</div>";
    }
}
