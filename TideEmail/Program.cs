// Daily tide report emailer for 136th Street, Ocean City, MD.
// Uses NOAA Tides and Currents API (station 8570283 — Ocean City Inlet).
// Weather from Open-Meteo (no API key required).

using DailyTidesWeather.Shared;
using TideEmail.Helpers;
using TideEmail.Services;

namespace TideEmail;

internal static class Program
{
    private static readonly (int Month, int Day) SeasonStart = (5, 17);  // May 17
    private static readonly (int Month, int Day) SeasonEnd   = (10, 15); // October 15

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

        var gmailAddress  = EnvHelper.Require("GMAIL_ADDRESS");
        var gmailPassword = EnvHelper.Require("GMAIL_APP_PASSWORD");
        var recipient     = EnvHelper.Require("RECIPIENT_EMAIL");

        Console.WriteLine($"Fetching NOAA tides for {today:yyyy-MM-dd}");
        var tides = await NoaaClient.FetchTides(today);

        Console.WriteLine("Fetching NOAA water temperature …");
        var (waterTemp, readingCount) = await NoaaClient.FetchWaterTemp(today);
        Console.WriteLine(waterTemp is not null
            ? $"  Water temp: {waterTemp.Value.ToString("0.0", Formatting.Inv)}°F (avg of {readingCount} lowest readings, last 24 hr)"
            : "  Water temp unavailable, continuing without it.");

        Console.WriteLine("Fetching Open-Meteo weather forecast");
        var weather = await OpenMeteoClient.FetchWeather(today);

        Console.WriteLine("Generating tide narrative via Claude");
        var narrative = await NarrativeGenerator.Generate(today, tides, weather, waterTemp);

        var subject = $"Tide Report — OC 136th St — {Formatting.ToDate(today).ToString("ddd MMM d", Formatting.Inv)}";
        var html    = EmailBuilder.BuildHtml(today, tides, narrative, weather, waterTemp, readingCount);
        var text    = EmailBuilder.BuildText(today, tides, narrative, weather, waterTemp, readingCount);

        Console.WriteLine($"Sending email to {recipient}");
        await EmailSender.Send(subject, html, text, gmailAddress, gmailPassword, recipient);
        Console.WriteLine("Done.");
    }

    private static bool InSeason(DateOnly today)
    {
        var start = new DateOnly(today.Year, SeasonStart.Month, SeasonStart.Day);
        var end   = new DateOnly(today.Year, SeasonEnd.Month, SeasonEnd.Day);
        return start <= today && today <= end;
    }
}
