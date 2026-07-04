using System.Text.Json;
using TideEmail.Helpers;
using TideEmail.Models;

namespace TideEmail.Services;

/// <summary>Open-Meteo hourly forecast for Ocean City, MD (no API key required).</summary>
internal static class OpenMeteoClient
{
    private const double OcLat = 38.3365;
    private const double OcLon = -75.0849;

    internal static async Task<List<WeatherHour>> FetchWeather(DateOnly today)
    {
        // Open-Meteo rejects %2C-encoded commas, so the hourly list is kept literal
        var dateStr = today.ToString("yyyy-MM-dd", Formatting.Inv);
        var url =
            "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={OcLat.ToString(Formatting.Inv)}&longitude={OcLon.ToString(Formatting.Inv)}"
            + "&hourly=temperature_2m,windspeed_10m,winddirection_10m,uv_index"
            + "&temperature_unit=fahrenheit&windspeed_unit=mph"
            + "&timezone=America%2FNew_York"
            + $"&start_date={dateStr}&end_date={dateStr}";
        using var doc = JsonDocument.Parse(await SharedHttp.Client.GetStringAsync(url));
        var hourly     = doc.RootElement.GetProperty("hourly");
        var times      = hourly.GetProperty("time");
        var temps      = hourly.GetProperty("temperature_2m");
        var windSpeeds = hourly.GetProperty("windspeed_10m");
        var windDirs   = hourly.GetProperty("winddirection_10m");
        var uvs        = hourly.GetProperty("uv_index");

        var rows = new List<WeatherHour>();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            var dt = DateTime.Parse(times[i].GetString()!, Formatting.Inv);
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
}
