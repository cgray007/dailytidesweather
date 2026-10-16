// Daily tide report emailer for Strands Beach, Dana Point, CA.
// Uses NOAA Tides and Currents API (station 9410580 — Dana Point, CA).
// Weather from Open-Meteo (no API key required).

using DailyTidesWeather.Shared;
using DanaPointEmail.Helpers;
using DanaPointEmail.Services;

namespace DanaPointEmail;

internal static class Program
{
    private static async Task Main()
    {
        // On local runs, persist any set secrets and hydrate any missing ones
        // from the per-user store; no-op in GitHub Actions.
        LocalSecrets.Sync();

        var today = DateOnly.FromDateTime(DateTime.Now);

        var gmailAddress  = EnvHelper.Require("GMAIL_ADDRESS");
        var gmailPassword = EnvHelper.Require("GMAIL_APP_PASSWORD");
        var recipient     = EnvHelper.Require("DANA_POINT_RECIPIENT_EMAIL");

        Console.WriteLine($"Fetching NOAA tides for {today:yyyy-MM-dd}");
        var tides = await NoaaClient.FetchTides(today);

        Console.WriteLine("Fetching NOAA water temperature …");
        var (waterTemp, readingCount) = await NoaaClient.FetchWaterTemp(today);
        Console.WriteLine(waterTemp is not null
            ? $"  Water temp: {waterTemp.Value.ToString("0.0", Formatting.Inv)}°F (avg of {readingCount} lowest readings, last 24 hr)"
            : "  Water temp unavailable, continuing without it.");

        Console.WriteLine("Fetching Open-Meteo weather forecast and sun times");
        var weather = await OpenMeteoClient.FetchWeather(today);
        var (sunrise, sunset) = await OpenMeteoClient.FetchSunTimes(today);

        var (moonName, moonEmoji, moonIllumination) = MoonPhase.Calculate(today);
        Console.WriteLine($"  Moon phase: {moonEmoji} {moonName} ({moonIllumination:0.0}% illuminated)");

        Console.WriteLine("Generating tide narrative via Claude");
        var narrative = await NarrativeGenerator.Generate(today, tides, weather, waterTemp, sunrise, sunset, moonName, moonIllumination);

        var subject = $"Tide Report — Dana Point Strands Beach — {Formatting.ToDate(today).ToString("ddd MMM d", Formatting.Inv)}";
        var html    = EmailBuilder.BuildHtml(today, tides, narrative, weather, waterTemp, readingCount, sunrise, sunset, moonName, moonEmoji, moonIllumination);
        var text    = EmailBuilder.BuildText(today, tides, narrative, weather, waterTemp, readingCount, sunrise, sunset, moonName, moonEmoji, moonIllumination);

        Console.WriteLine($"Sending email to {recipient}");
        await EmailSender.Send(subject, html, text, gmailAddress, gmailPassword, recipient);
        Console.WriteLine("Done.");
    }
}
