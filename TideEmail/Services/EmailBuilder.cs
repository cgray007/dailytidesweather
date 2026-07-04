using System.Text;
using TideEmail.Helpers;
using TideEmail.Models;

namespace TideEmail.Services;

/// <summary>Builds the HTML and plain-text bodies of the tide report email.</summary>
internal static class EmailBuilder
{
    // Source URLs — used for clickable links in the HTML email
    private const string Lk = "target='_blank' rel='noopener noreferrer'"; // shared link attributes
    private const string UrlNoaaTides   = "https://tidesandcurrents.noaa.gov/noaatidepredictions.html?id=8570283";
    private const string UrlNoaaWater   = "https://tidesandcurrents.noaa.gov/physocean.html?id=8570283";
    private const string UrlNoaaStation = "https://tidesandcurrents.noaa.gov/stationhome.html?id=8570283";
    private const string UrlEpaUv       = "https://www.epa.gov/sunsafety/uv-index-scale-0";
    private const string UrlOpenMeteo   = "https://open-meteo.com/";

    internal static string BuildHtml(DateOnly today, List<TidePrediction> tides, string narrative,
                                     List<WeatherHour> weather, double? waterTemp, int readingCount)
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

    internal static string BuildText(DateOnly today, List<TidePrediction> tides, string narrative,
                                     List<WeatherHour> weather, double? waterTemp, int readingCount)
    {
        var dateLabel = Formatting.ToDate(today).ToString("dddd, MMMM d, yyyy", Formatting.Inv);
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
            lines.Add($"Water Temp: {waterTemp.Value.ToString("0.0", Formatting.Inv)}°F (avg of {readingCount} lowest readings in 24 hrs, Ocean City Inlet)");
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
            var wind = $"{Formatting.WindDirLabel(w.WindDir)} {w.WindSpeed.ToString("0", Formatting.Inv)} mph";
            lines.Add(
                $"  {w.Hour.ToString("h tt", Formatting.Inv),-7}  {w.Temp.ToString("0", Formatting.Inv)}°F    {wind,-16}  "
                + $"{w.Uv.ToString("0.0", Formatting.Inv)} ({Formatting.UvLabel(w.Uv)})");
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
                   + $"(avg of {readingCount} lowest readings in 24 hrs, Ocean City Inlet)</span>";
        return "<div style='margin:0 28px 16px;padding:12px 16px;"
               + "background:#e8f4fd;border-left:4px solid #0a3d6b;border-radius:4px;"
               + "font-size:15px;color:#333;'>"
               + $"<a href='{UrlNoaaWater}' {Lk} style='font-weight:bold;color:#333;text-decoration:none;'>"
               + "🌊 Water Temp:</a> "
               + $"<span style='font-size:18px;font-weight:bold;color:#0a3d6b;'>{waterTemp.Value.ToString("0.0", Formatting.Inv)}°F</span>"
               + $"{note}</div>";
    }
}
