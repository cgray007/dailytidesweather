using DailyTidesWeather.Shared;

namespace TideEmail.Helpers;

internal static class EnvHelper
{
    internal static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Missing required environment variable: {name}. "
            + $"Set it in the environment or add it to {LocalSecrets.SettingsPath}.");
}
