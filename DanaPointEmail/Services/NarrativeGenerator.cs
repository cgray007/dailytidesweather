using System.Text;
using Anthropic;
using Anthropic.Models.Beta.Sessions;
using Anthropic.Models.Beta.Sessions.Events;
using DanaPointEmail.Helpers;
using DanaPointEmail.Models;

namespace DanaPointEmail.Services;

/// <summary>Generates the morning beach briefing via the OC Tide Narrator Managed Agent.</summary>
internal static class NarrativeGenerator
{
    internal static async Task<string> Generate(DateOnly today, List<TidePrediction> tides,
                                                List<WeatherHour> weather, double? waterTemp,
                                                string? sunrise, string? sunset,
                                                string moonName, double moonIllumination)
    {
        var waterLine = waterTemp is not null
            ? $"\nOcean water temperature (avg of the 10 lowest readings over the last 24 hours): {waterTemp.Value.ToString("0.0", Formatting.Inv)}°F"
            : "";
        var sunLine = (sunrise is not null || sunset is not null)
            ? $"\nSunrise: {sunrise ?? "N/A"}  |  Sunset: {sunset ?? "N/A"}"
            : "";
        var moonLine = $"\nMoon phase: {moonName} ({moonIllumination.ToString("0.0", Formatting.Inv)}% illuminated)";
        var message =
            $"Today is {Formatting.ToDate(today).ToString("dddd, MMMM d, yyyy", Formatting.Inv)}. "
            + "Here are today's tide predictions for Strands Beach, Dana Point, CA:\n"
            + string.Join("\n", TideSummaryLines(tides))
            + waterLine
            + sunLine
            + moonLine
            + "\n\nWeather forecast for Dana Point, CA:\n" + string.Join("\n", WeatherSummaryLines(weather));

        var client = new AnthropicClient();
        var session = await client.Beta.Sessions.Create(new SessionCreateParams
        {
            Agent = EnvHelper.Require("TIDE_AGENT_ID"),
            EnvironmentID = EnvHelper.Require("TIDE_ENV_ID"),
        });

        var parts = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

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

    private static IEnumerable<string> TideSummaryLines(List<TidePrediction> tides) =>
        tides.Select(entry =>
        {
            var label = entry.Type == "H" ? "High" : "Low";
            return $"  {label} tide at {entry.Time.ToString("h:mm tt", Formatting.Inv)}: {Formatting.Height(entry.Height)}";
        });

    private static IEnumerable<string> WeatherSummaryLines(List<WeatherHour> weather) =>
        weather.Select(w =>
            $"  {w.Hour.ToString("h tt", Formatting.Inv)}: {w.Temp.ToString("0", Formatting.Inv)}°F, "
            + $"Wind {Formatting.WindDirLabel(w.WindDir)} {w.WindSpeed.ToString("0", Formatting.Inv)} mph, "
            + $"Precip {w.PrecipChance.ToString("0", Formatting.Inv)}%, "
            + $"Clouds {w.CloudCover.ToString("0", Formatting.Inv)}%, "
            + $"UV {w.Uv.ToString("0.0", Formatting.Inv)} ({Formatting.UvLabel(w.Uv)})");
}
