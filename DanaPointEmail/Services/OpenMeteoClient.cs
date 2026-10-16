using System.Text.Json;
using DanaPointEmail.Helpers;
using DanaPointEmail.Models;

namespace DanaPointEmail.Services;

/// <summary>Open-Meteo hourly forecast for Dana Point, CA (no API key required).</summary>
internal static class OpenMeteoClient
{
    private const double DpLat = 33.4672;
    private const double DpLon = -117.6981;

    internal static async Task<List<WeatherHour>> FetchWeather(DateOnly today)
    {
        // Open-Meteo rejects %2C-encoded commas, so the hourly list is kept literal
        var dateStr = today.ToString("yyyy-MM-dd", Formatting.Inv);
        var url =
            "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={DpLat.ToString(Formatting.Inv)}&longitude={DpLon.ToString(Formatting.Inv)}"
            + "&hourly=temperature_2m,windspeed_10m,winddirection_10m,uv_index,precipitation_probability,cloudcover"
            + "&temperature_unit=fahrenheit&windspeed_unit=mph"
            + "&timezone=America%2FLos_Angeles"
            + $"&start_date={dateStr}&end_date={dateStr}";
        using var doc = JsonDocument.Parse(await SharedHttp.Client.GetStringAsync(url));
        var hourly     = doc.RootElement.GetProperty("hourly");
        var times      = hourly.GetProperty("time");
        var temps      = hourly.GetProperty("temperature_2m");
        var windSpeeds = hourly.GetProperty("windspeed_10m");
        var windDirs   = hourly.GetProperty("winddirection_10m");
        var uvs        = hourly.GetProperty("uv_index");
        var precips    = hourly.GetProperty("precipitation_probability");
        var clouds     = hourly.GetProperty("cloudcover");

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
                    uvs[i].GetDouble(),
                    precips[i].ValueKind is JsonValueKind.Null ? 0 : precips[i].GetDouble(),
                    clouds[i].ValueKind is JsonValueKind.Null ? 0 : clouds[i].GetDouble()));
            }
        }
        return rows;
    }

    internal static async Task<(string? Sunrise, string? Sunset)> FetchSunTimes(DateOnly today)
    {
        var dateStr = today.ToString("yyyy-MM-dd", Formatting.Inv);
        var url =
            "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={DpLat.ToString(Formatting.Inv)}&longitude={DpLon.ToString(Formatting.Inv)}"
            + "&daily=sunrise,sunset"
            + "&timezone=America%2FLos_Angeles"
            + $"&start_date={dateStr}&end_date={dateStr}";
        using var doc = JsonDocument.Parse(await SharedHttp.Client.GetStringAsync(url));
        var daily  = doc.RootElement.GetProperty("daily");
        var srStr  = daily.GetProperty("sunrise")[0].GetString();
        var ssStr  = daily.GetProperty("sunset")[0].GetString();
        var sunrise = srStr is not null ? DateTime.Parse(srStr, Formatting.Inv).ToString("h:mm tt", Formatting.Inv) : null;
        var sunset  = ssStr is not null ? DateTime.Parse(ssStr, Formatting.Inv).ToString("h:mm tt", Formatting.Inv) : null;
        return (sunrise, sunset);
    }
}
