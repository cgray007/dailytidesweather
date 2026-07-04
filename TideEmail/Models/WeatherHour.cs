namespace TideEmail.Models;

/// <summary>One hour of Open-Meteo forecast: temperature (°F), wind speed (mph), wind direction (°), UV index.</summary>
internal sealed record WeatherHour(DateTime Hour, double Temp, double WindSpeed, double WindDir, double Uv);
