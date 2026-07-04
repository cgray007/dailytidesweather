using System.Text;
using Anthropic;
using Anthropic.Models.Beta.Sessions;
using Anthropic.Models.Beta.Sessions.Events;
using TideEmail.Helpers;
using TideEmail.Models;

namespace TideEmail.Services;

/// <summary>Generates the morning beach briefing via the OC Tide Narrator Managed Agent.</summary>
internal static class NarrativeGenerator
{
    internal static async Task<string> Generate(DateOnly today, List<TidePrediction> tides,
                                                List<WeatherHour> weather, double? waterTemp)
    {
        var waterLine = waterTemp is not null
            ? $"\nOcean water temperature (avg of the lowest readings over the last 24 hours): {waterTemp.Value.ToString("0.0", Formatting.Inv)}°F"
            : "";
        var message =
            $"Today is {Formatting.ToDate(today).ToString("dddd, MMMM d, yyyy", Formatting.Inv)}. "
            + "Here are today's tide predictions:\n" + string.Join("\n", TideSummaryLines(tides))
            + waterLine
            + "\n\nWeather forecast for Ocean City, MD:\n" + string.Join("\n", WeatherSummaryLines(weather));

        var client = new AnthropicClient(); // reads ANTHROPIC_API_KEY from env
        var session = await client.Beta.Sessions.Create(new SessionCreateParams
        {
            Agent = EnvHelper.Require("TIDE_AGENT_ID"),
            EnvironmentID = EnvHelper.Require("TIDE_ENV_ID"),
        });

        var parts = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        // Open the event stream before sending so no early events are missed.
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

        // Give the SSE connection a moment to establish, then kick off the agent.
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
            + $"UV {w.Uv.ToString("0.0", Formatting.Inv)} ({Formatting.UvLabel(w.Uv)})");
}
