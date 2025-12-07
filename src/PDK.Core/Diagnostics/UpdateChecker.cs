using System.Text.Json;
using System.Text.Json.Serialization;

namespace PDK.Core.Diagnostics;

/// <summary>
/// Interface for checking PDK updates.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Determines whether an update check should be performed.
    /// </summary>
    /// <returns>True if an update check should be performed; otherwise, false.</returns>
    bool ShouldCheckForUpdates();

    /// <summary>
    /// Checks NuGet for available updates.
    /// </summary>
    /// <param name="currentVersion">The current PDK version.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Update information if an update is available; otherwise, null.</returns>
    Task<UpdateInfo?> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last check timestamp to throttle future checks.
    /// </summary>
    Task UpdateLastCheckTimeAsync();
}

/// <summary>
/// Checks for PDK updates from NuGet.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private const string UpdateCheckFileName = "update-check.json";
    private const string NuGetPackageId = "pdk";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly string _updateCheckFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class.
    /// </summary>
    public UpdateChecker() : this(new HttpClient())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public UpdateChecker(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        var pdkDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".pdk");
        _updateCheckFilePath = Path.Combine(pdkDir, UpdateCheckFileName);
    }

    /// <summary>
    /// Determines whether an update check should be performed.
    /// </summary>
    /// <returns>True if an update check should be performed; otherwise, false.</returns>
    public bool ShouldCheckForUpdates()
    {
        // Never check in CI environments
        if (CiDetector.IsRunningInCi())
        {
            return false;
        }

        // Check if throttle period has passed
        if (!File.Exists(_updateCheckFilePath))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(_updateCheckFilePath);
            var data = JsonSerializer.Deserialize<UpdateCheckData>(json);
            if (data?.LastCheck == null)
            {
                return true;
            }

            return DateTime.UtcNow - data.LastCheck.Value > CheckInterval;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Checks NuGet for available updates.
    /// </summary>
    /// <param name="currentVersion">The current PDK version.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Update information if an update is available; otherwise, null.</returns>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            var url = $"https://api.nuget.org/v3-flatcontainer/{NuGetPackageId.ToLowerInvariant()}/index.json";
            var response = await _httpClient.GetStringAsync(url, cts.Token);

            var versions = JsonSerializer.Deserialize<NuGetVersionsResponse>(response);
            var latestVersionString = versions?.Versions?.LastOrDefault();

            if (string.IsNullOrEmpty(latestVersionString))
            {
                return null;
            }

            // Parse versions for comparison
            var currentVersionClean = CleanVersionString(currentVersion);
            if (!Version.TryParse(currentVersionClean, out var currentVer))
            {
                return null;
            }

            if (!Version.TryParse(latestVersionString, out var latestVer))
            {
                return null;
            }

            if (latestVer > currentVer)
            {
                return new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    LatestVersion = latestVersionString,
                    IsUpdateAvailable = true,
                    UpdateCommand = "dotnet tool update -g pdk"
                };
            }

            return null;
        }
        catch
        {
            // Fail gracefully - update check is non-critical
            return null;
        }
    }

    /// <summary>
    /// Updates the last check timestamp to throttle future checks.
    /// </summary>
    public async Task UpdateLastCheckTimeAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_updateCheckFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var data = new UpdateCheckData { LastCheck = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(_updateCheckFilePath, json);
        }
        catch
        {
            // Ignore errors - throttle file is non-critical
        }
    }

    private static string CleanVersionString(string version)
    {
        // Remove commit hash suffix (e.g., "1.0.0+abc123" -> "1.0.0")
        var plusIndex = version.IndexOf('+');
        if (plusIndex >= 0)
        {
            version = version[..plusIndex];
        }

        // Remove pre-release suffix (e.g., "1.0.0-beta1" -> "1.0.0")
        var dashIndex = version.IndexOf('-');
        if (dashIndex >= 0)
        {
            version = version[..dashIndex];
        }

        return version;
    }

    private sealed class UpdateCheckData
    {
        [JsonPropertyName("lastCheck")]
        public DateTime? LastCheck { get; set; }
    }

    private sealed class NuGetVersionsResponse
    {
        [JsonPropertyName("versions")]
        public List<string>? Versions { get; set; }
    }
}
