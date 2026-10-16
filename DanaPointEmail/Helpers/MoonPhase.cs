namespace DanaPointEmail.Helpers;

/// <summary>Calculates moon phase and illumination percentage from the date alone (no API required).</summary>
internal static class MoonPhase
{
    private const double SynodicPeriod = 29.53058867;

    private static readonly DateTime ReferenceNewMoon =
        new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    internal static (string Name, string Emoji, double Illumination) Calculate(DateOnly date)
    {
        var noon = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);
        var elapsed = (noon - ReferenceNewMoon).TotalDays % SynodicPeriod;
        if (elapsed < 0) elapsed += SynodicPeriod;

        var fraction     = elapsed / SynodicPeriod;
        var illumination = (1 - Math.Cos(2 * Math.PI * fraction)) / 2 * 100;

        var (name, emoji) = fraction switch
        {
            < 0.0625 or >= 0.9375 => ("New Moon",        "🌑"),
            < 0.1875              => ("Waxing Crescent",  "🌒"),
            < 0.3125              => ("First Quarter",    "🌓"),
            < 0.4375              => ("Waxing Gibbous",   "🌔"),
            < 0.5625              => ("Full Moon",        "🌕"),
            < 0.6875              => ("Waning Gibbous",   "🌖"),
            < 0.8125              => ("Third Quarter",    "🌗"),
            _                    => ("Waning Crescent",  "🌘"),
        };

        return (name, emoji, illumination);
    }
}
