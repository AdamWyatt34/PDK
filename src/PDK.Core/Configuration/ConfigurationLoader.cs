namespace PDK.Core.Configuration;

using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Provides functionality to discover, load, and validate configuration files.
/// </summary>
public class ConfigurationLoader : IConfigurationLoader
{
    private readonly ILogger<ConfigurationLoader> _logger;
    private readonly ConfigurationValidator _validator;

    /// <summary>
    /// JSON serializer options for reading configuration files.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Configuration file names to search for, in order of preference.
    /// </summary>
    private static readonly string[] ConfigFileNames =
    [
        ".pdkrc",
        "pdk.config.json"
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationLoader"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="validator">The configuration validator.</param>
    public ConfigurationLoader(ILogger<ConfigurationLoader> logger, ConfigurationValidator validator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationLoader"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ConfigurationLoader(ILogger<ConfigurationLoader> logger)
        : this(logger, new ConfigurationValidator())
    {
    }

    /// <inheritdoc/>
    public async Task<PdkConfig?> LoadAsync(string? configPath = null, CancellationToken cancellationToken = default)
    {
        string? filePath;

        if (!string.IsNullOrEmpty(configPath))
        {
            // Explicit path provided - must exist
            filePath = ExpandPath(configPath);
            if (!File.Exists(filePath))
            {
                throw ConfigurationException.FileNotFound(filePath);
            }
        }
        else
        {
            // Discover configuration file
            filePath = DiscoverConfigFile();
            if (filePath == null)
            {
                _logger.LogDebug("No configuration file found, using defaults");
                return null;
            }
        }

        _logger.LogDebug("Loading configuration from {FilePath}", filePath);
        return await LoadAndValidateAsync(filePath, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateAsync(string configPath, CancellationToken cancellationToken = default)
    {
        var expandedPath = ExpandPath(configPath);

        if (!File.Exists(expandedPath))
        {
            return false;
        }

        try
        {
            var config = await LoadFromFileAsync(expandedPath, cancellationToken);
            var result = _validator.Validate(config);
            return result.IsValid;
        }
        catch (Exception ex) when (ex is JsonException or ConfigurationException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public string? DiscoverConfigFile()
    {
        return DiscoverConfigFile(Environment.CurrentDirectory);
    }

    /// <inheritdoc/>
    public string? DiscoverConfigFile(string startDirectory)
    {
        // Search in current directory first
        foreach (var fileName in ConfigFileNames)
        {
            var path = Path.Combine(startDirectory, fileName);
            if (File.Exists(path))
            {
                _logger.LogDebug("Discovered configuration file: {FilePath}", path);
                return path;
            }
        }

        // Search in home directory
        var homeDir = GetHomeDirectory();
        if (!string.IsNullOrEmpty(homeDir))
        {
            // Check ~/.pdkrc
            var homePdkrc = Path.Combine(homeDir, ".pdkrc");
            if (File.Exists(homePdkrc))
            {
                _logger.LogDebug("Discovered configuration file: {FilePath}", homePdkrc);
                return homePdkrc;
            }

            // Check ~/.pdk/config.json
            var homePdkConfig = Path.Combine(homeDir, ".pdk", "config.json");
            if (File.Exists(homePdkConfig))
            {
                _logger.LogDebug("Discovered configuration file: {FilePath}", homePdkConfig);
                return homePdkConfig;
            }
        }

        return null;
    }

    private async Task<PdkConfig> LoadAndValidateAsync(string filePath, CancellationToken cancellationToken)
    {
        var config = await LoadFromFileAsync(filePath, cancellationToken);

        var validationResult = _validator.Validate(config);
        if (!validationResult.IsValid)
        {
            throw ConfigurationException.ValidationFailed(filePath, validationResult.Errors);
        }

        return config;
    }

    /// <summary>
    /// Options used when inspecting the raw document (same leniency as <see cref="JsonOptions"/>).
    /// </summary>
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static async Task<PdkConfig> LoadFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);

            // The model defaults Version to "1.0", so a missing field has to be detected on the raw document.
            EnsureVersionPresent(filePath, json);

            var config = JsonSerializer.Deserialize<PdkConfig>(json, JsonOptions);

            return config ?? new PdkConfig();
        }
        catch (JsonException ex)
        {
            throw ConfigurationException.InvalidJson(filePath, ex);
        }
    }

    /// <summary>
    /// Fails with a clear message when the top-level <c>version</c> field is absent.
    /// </summary>
    private static void EnsureVersionPresent(string filePath, string json)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw ConfigurationException.InvalidJson(
                filePath,
                new JsonException("The configuration root must be a JSON object."));
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw ConfigurationException.MissingVersion(filePath);
    }

    /// <summary>
    /// Expands a leading tilde to the user's home directory. Only <c>~</c>, <c>~/...</c> and
    /// <c>~\...</c> are expanded; other paths (including <c>~user/...</c> and names that merely
    /// start with a tilde) are returned unchanged.
    /// </summary>
    /// <param name="path">The path to expand.</param>
    /// <returns>The expanded path.</returns>
    public static string ExpandPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var home = GetHomeDirectory();
        if (string.IsNullOrEmpty(home))
        {
            return path;
        }

        if (path == "~")
        {
            return home;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(home, path[2..]);
        }

        return path;
    }

    private static string GetHomeDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
