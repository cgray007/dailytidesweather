namespace TideEmail.Models;

/// <summary>One hour of Open-Meteo forecast: temperature (°F), wind speed (mph), wind direction (°), UV index, chance of precipitation (%), cloud cover (%).</summary>
internal sealed record WeatherHour(DateTime Hour, double Temp, double WindSpeed, double WindDir, double Uv, double PrecipChance, double CloudCover);
