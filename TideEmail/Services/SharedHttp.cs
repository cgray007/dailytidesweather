namespace TideEmail.Services;

/// <summary>Single HttpClient shared by the data-fetching services.</summary>
internal static class SharedHttp
{
    internal static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
}
