// Daily tide report emailer for 136th Street, Ocean City, MD.
// Uses NOAA Tides and Currents API (station 8570283 — Ocean City Inlet).
// Weather from Open-Meteo (no API key required).

using System.Text.Json;
using DailyTidesWeather.Shared;
using TideEmail.Helpers;
using TideEmail.Services;

namespace TideEmail;

internal static class Program
{
    private static async Task Main()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (!InSeason(today))
        {
            Console.WriteLine($"Outside season ({today:yyyy-MM-dd}). No email sent.");
            return;
        }

        // On local runs, persist any set secrets and hydrate any missing ones
        // from the per-user store; no-op in GitHub Actions.
        LocalSecrets.Sync();

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

        Console.WriteLine("Fetching Open-Meteo weather forecast and sun times");
        var weather = await OpenMeteoClient.FetchWeather(today);
        var (sunrise, sunset) = await OpenMeteoClient.FetchSunTimes(today);

        var (moonName, moonEmoji, moonIllumination) = MoonPhase.Calculate(today);
        Console.WriteLine($"  Moon phase: {moonEmoji} {moonName} ({moonIllumination:0.0}% illuminated)");

        Console.WriteLine("Generating tide narrative via Claude");
        var narrative = await NarrativeGenerator.Generate(today, tides, weather, waterTemp, sunrise, sunset, moonName, moonIllumination);

        var subject = $"Tide Report — OC 136th St — {Formatting.ToDate(today).ToString("ddd MMM d", Formatting.Inv)}";
        var html    = EmailBuilder.BuildHtml(today, tides, narrative, weather, waterTemp, readingCount, sunrise, sunset, moonName, moonEmoji, moonIllumination);
        var text    = EmailBuilder.BuildText(today, tides, narrative, weather, waterTemp, readingCount, sunrise, sunset, moonName, moonEmoji, moonIllumination);

        Console.WriteLine($"Sending email to {recipient}");
        await EmailSender.Send(subject, html, text, gmailAddress, gmailPassword, recipient);
        Console.WriteLine("Done.");
    }

    // appsettings.json Schedule:FromDate / ToDate are "MMdd" strings, both inclusive.
    private static bool InSeason(DateOnly today)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var schedule = doc.RootElement.GetProperty("Schedule");

        var from = ParseMonthDay(schedule.GetProperty("FromDate"));
        var to   = ParseMonthDay(schedule.GetProperty("ToDate"));
        var now  = today.Month * 100 + today.Day;

        // from <= to is a normal in-year range; otherwise the range wraps over New Year.
        return from <= to ? from <= now && now <= to : now >= from || now <= to;
    }

    private static int ParseMonthDay(JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.Number ? value.GetInt32().ToString("D4") : value.GetString() ?? "";
        if (!DateOnly.TryParseExact("2000" + text, "yyyyMMdd", out var date))
            throw new InvalidOperationException($"Invalid Schedule date '{text}' in appsettings.json; expected MMdd, e.g. \"0501\".");
        return date.Month * 100 + date.Day;
    }
}
