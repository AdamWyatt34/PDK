using System.Text.Json;
using System.Text.Json.Serialization;

namespace PDK.Core.Diagnostics;

/// <summary>
/// Interface for checking PDK updates.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Determines whether an update check should be performed (not in CI, and the
    /// throttle period since the last successful check has elapsed).
    /// </summary>
    /// <returns>True if an update check should be performed; otherwise, false.</returns>
    bool ShouldCheckForUpdates();

    /// <summary>
    /// Determines whether an update check should be performed, honouring the
    /// <c>features.checkUpdates</c> configuration value.
    /// </summary>
    /// <param name="checkUpdatesEnabled">The configured value (<c>PdkConfig.Features.CheckUpdates</c>); false disables the check.</param>
    /// <returns>True if an update check should be performed; otherwise, false.</returns>
    bool ShouldCheckForUpdates(bool checkUpdatesEnabled) => checkUpdatesEnabled && ShouldCheckForUpdates();

    /// <summary>
    /// Checks NuGet for available updates.
    /// </summary>
    /// <param name="currentVersion">The current PDK version.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Update information if a newer stable version is available; otherwise, null.</returns>
    Task<UpdateInfo?> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last check timestamp to throttle future checks.
    /// Implementations only record a timestamp after a successful check, so a failed
    /// (offline) check is retried on the next invocation.
    /// </summary>
    Task UpdateLastCheckTimeAsync();
}

/// <summary>
/// Checks for PDK updates from NuGet.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    /// <summary>
    /// Name of the throttle state file stored below <c>~/.pdk</c>.
    /// </summary>
    public const string UpdateCheckFileName = "update-check.json";

    private const string NuGetPackageId = "pdk";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly string _updateCheckFilePath;
    private bool _lastCheckSucceeded;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class using the default state file.
    /// </summary>
    public UpdateChecker() : this(new HttpClient(), null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    public UpdateChecker(HttpClient httpClient) : this(httpClient, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests.</param>
    /// <param name="stateFilePath">
    /// Path of the throttle state file. Null selects <see cref="DefaultStateFilePath"/>
    /// (<c>~/.pdk/update-check.json</c>); tests pass a temporary path to stay isolated.
    /// </param>
    public UpdateChecker(HttpClient httpClient, string? stateFilePath)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _updateCheckFilePath = string.IsNullOrWhiteSpace(stateFilePath) ? DefaultStateFilePath : stateFilePath;
    }

    /// <summary>
    /// Gets the default throttle state file path (<c>~/.pdk/update-check.json</c>).
    /// </summary>
    public static string DefaultStateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pdk",
        UpdateCheckFileName);

    /// <summary>
    /// Gets the throttle state file used by this instance.
    /// </summary>
    public string StateFilePath => _updateCheckFilePath;

    /// <summary>
    /// Gets whether the most recent <see cref="CheckForUpdatesAsync"/> call reached NuGet and parsed the response.
    /// </summary>
    public bool LastCheckSucceeded => _lastCheckSucceeded;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<UpdateInfo?> CheckForUpdatesAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        _lastCheckSucceeded = false;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            var url = $"https://api.nuget.org/v3-flatcontainer/{NuGetPackageId.ToLowerInvariant()}/index.json";
            var response = await _httpClient.GetStringAsync(url, cts.Token);

            var versions = JsonSerializer.Deserialize<NuGetVersionsResponse>(response);
            if (versions?.Versions == null)
            {
                return null;
            }

            // The response was fetched and parsed: the throttle timestamp may be recorded.
            _lastCheckSucceeded = true;

            var latestStable = SelectLatestStableVersion(versions.Versions);
            if (latestStable == null)
            {
                return null;
            }

            if (!TryParseVersion(currentVersion, out var currentVer) ||
                !TryParseVersion(latestStable, out var latestVer))
            {
                return null;
            }

            if (latestVer > currentVer)
            {
                return new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    LatestVersion = latestStable,
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

    /// <inheritdoc/>
    public async Task UpdateLastCheckTimeAsync()
    {
        if (!_lastCheckSucceeded)
        {
            // A failed check must not start the throttle period; retry on the next run.
            return;
        }

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

    /// <summary>
    /// Selects the highest stable (non-prerelease) version from a NuGet version list.
    /// The list order is not trusted: every entry is parsed and compared numerically.
    /// </summary>
    /// <param name="versions">Version strings as returned by the NuGet flat container index.</param>
    /// <returns>The highest stable version string, or null when the list has no stable version.</returns>
    public static string? SelectLatestStableVersion(IEnumerable<string> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        string? bestText = null;
        Version? best = null;

        foreach (var text in versions)
        {
            if (string.IsNullOrWhiteSpace(text) || IsPrerelease(text))
            {
                continue;
            }

            if (!TryParseVersion(text, out var parsed))
            {
                continue;
            }

            if (best == null || parsed > best)
            {
                best = parsed;
                bestText = text.Trim();
            }
        }

        return bestText;
    }

    /// <summary>
    /// Determines whether a version string denotes a prerelease (SemVer <c>-suffix</c>).
    /// </summary>
    public static bool IsPrerelease(string version)
    {
        var withoutMetadata = StripBuildMetadata(version);
        return withoutMetadata.Contains('-');
    }

    private static bool TryParseVersion(string version, out Version parsed)
    {
        parsed = new Version(0, 0, 0, 0);

        var cleaned = CleanVersionString(version);
        if (!Version.TryParse(cleaned, out var raw))
        {
            return false;
        }

        // Normalise to four components so that "1.0" and "1.0.0" compare as equal.
        parsed = new Version(
            Math.Max(raw.Major, 0),
            Math.Max(raw.Minor, 0),
            Math.Max(raw.Build, 0),
            Math.Max(raw.Revision, 0));
        return true;
    }

    private static string StripBuildMetadata(string version)
    {
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    private static string CleanVersionString(string version)
    {
        version = StripBuildMetadata(version.Trim());

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
