namespace TideEmail.Models;

/// <summary>A single NOAA high/low tide prediction. Type is "H" or "L"; height is in feet above MLLW.</summary>
internal sealed record TidePrediction(DateTime Time, string Type, double Height);
