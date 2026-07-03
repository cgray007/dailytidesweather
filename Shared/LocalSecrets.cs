// Local secrets persistence for developer machines.
//
// When the app runs locally (i.e. not in GitHub Actions/CI), Sync():
//   1. writes any of the known environment variables that are currently set
//      into a per-user JSON file, and
//   2. loads any that are NOT set from that file back into the process
//      environment, so the rest of the app (including the Anthropic SDK's
//      ANTHROPIC_API_KEY lookup) works without exporting them every time.
//
// In CI the method is a no-op — secrets come exclusively from the workflow env.

using System.Text.Json;

namespace DailyTidesWeather.Shared;

internal static class LocalSecrets
{
    private static readonly string[] Keys =
    [
        "ANTHROPIC_API_KEY",
        "GMAIL_ADDRESS",
        "GMAIL_APP_PASSWORD",
        "RECIPIENT_EMAIL",
        "TIDE_AGENT_ID",
        "TIDE_ENV_ID",
    ];

    internal static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DailyTidesWeather", "secrets.json");

    private static bool IsCi =>
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
        || Environment.GetEnvironmentVariable("CI") == "true";

    public static void Sync()
    {
        if (IsCi)
            return;

        var path = SettingsPath;
        var stored = Load(path);
        var changed = false;
        var hydrated = new List<string>();

        // A gitignored secrets.local.json in the project/repo tree seeds the
        // per-user store. It is deleted after a successful import so secrets
        // don't linger inside the repo folder.
        var bootstrapPath = ImportBootstrap(stored);
        changed |= bootstrapPath is not null;

        foreach (var key in Keys)
        {
            var envValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(envValue))
            {
                if (!stored.TryGetValue(key, out var existing) || existing != envValue)
                {
                    stored[key] = envValue;
                    changed = true;
                }
            }
            else if (stored.TryGetValue(key, out var savedValue) && !string.IsNullOrEmpty(savedValue))
            {
                Environment.SetEnvironmentVariable(key, savedValue);
                hydrated.Add(key);
            }
        }

        if (changed)
        {
            Save(path, stored);
            Console.WriteLine($"Saved local secrets to {path}");
            if (bootstrapPath is not null)
            {
                try
                {
                    File.Delete(bootstrapPath);
                    Console.WriteLine($"Deleted bootstrap file {bootstrapPath} after import.");
                }
                catch
                {
                    Console.WriteLine($"Could not delete {bootstrapPath} — please remove it manually.");
                }
            }
        }
        if (hydrated.Count > 0)
            Console.WriteLine($"Loaded {string.Join(", ", hydrated)} from {path}");
    }

    /// <summary>
    /// Looks for a secrets.local.json bootstrap file from the app's base
    /// directory upward (bin/Debug/… → project → repo root). If found, merges
    /// its values into <paramref name="stored"/> and hydrates the environment.
    /// Returns the file's path when values were imported, otherwise null.
    /// </summary>
    private static string? ImportBootstrap(Dictionary<string, string> stored)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 6; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "secrets.local.json");
            if (!File.Exists(candidate))
                continue;

            var imported = Load(candidate);
            var any = false;
            foreach (var (key, value) in imported)
            {
                if (string.IsNullOrEmpty(value))
                    continue;
                stored[key] = value;
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    Environment.SetEnvironmentVariable(key, value);
                any = true;
            }
            if (any)
            {
                Console.WriteLine($"Imported secrets from {candidate}");
                return candidate;
            }
            return null;
        }
        return null;
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            // A corrupt settings file shouldn't stop the run — start fresh.
        }
        return [];
    }

    private static void Save(string path, Dictionary<string, string> stored)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 600 — secrets are per-user
    }
}
