using System.Globalization;

namespace TideEmail.Helpers;

/// <summary>Culture-invariant formatting and unit-labeling helpers shared across the report.</summary>
internal static class Formatting
{
    internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    internal static DateTime ToDate(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    internal static string Height(double height) => height.ToString("+0.00;-0.00", Inv) + " ft";

    internal static string WindDirLabel(double deg)
    {
        string[] dirs = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                         "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"];
        return dirs[(int)Math.Round(deg / 22.5, MidpointRounding.ToEven) % 16];
    }

    internal static string UvLabel(double uv) => uv switch
    {
        < 3  => "Low",
        < 6  => "Moderate",
        < 8  => "High",
        < 11 => "Very High",
        _    => "Extreme",
    };

    internal static string UvColor(double uv) => uv switch
    {
        < 3  => "#2d8a2d",
        < 6  => "#b8860b",
        < 8  => "#d4600a",
        < 11 => "#c0392b",
        _    => "#8e44ad",
    };
}
