namespace PDK.Core.Configuration;

/// <summary>
/// Provides functionality to discover, load, and validate configuration files.
/// </summary>
public interface IConfigurationLoader
{
    /// <summary>
    /// Loads configuration from a file path or discovers the configuration file automatically.
    /// </summary>
    /// <param name="configPath">
    /// Optional explicit path to the configuration file.
    /// If null, the loader will discover the configuration file using the standard search order.
    /// If provided but the file doesn't exist, throws ConfigurationException.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The loaded and validated configuration, or null if no configuration file was found during discovery.
    /// </returns>
    /// <exception cref="ConfigurationException">
    /// Thrown when the specified file is not found, contains invalid JSON, or fails validation.
    /// </exception>
    Task<PdkConfig?> LoadAsync(string? configPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a configuration file without fully loading it.
    /// </summary>
    /// <param name="configPath">The path to the configuration file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the configuration is valid, false otherwise.</returns>
    Task<bool> ValidateAsync(string configPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers a configuration file using the standard search order.
    /// </summary>
    /// <returns>The path to the discovered configuration file, or null if none found.</returns>
    /// <remarks>
    /// Search order:
    /// 1. .pdkrc in current directory
    /// 2. pdk.config.json in current directory
    /// 3. ~/.pdkrc in user home directory
    /// 4. ~/.pdk/config.json in user home directory
    /// </remarks>
    string? DiscoverConfigFile();

    /// <summary>
    /// Discovers a configuration file using the standard search order, starting from a specific directory.
    /// </summary>
    /// <param name="startDirectory">The directory to start searching from.</param>
    /// <returns>The path to the discovered configuration file, or null if none found.</returns>
    string? DiscoverConfigFile(string startDirectory);
}
