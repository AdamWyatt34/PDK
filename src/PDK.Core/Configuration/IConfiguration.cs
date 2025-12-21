namespace PDK.Core.Configuration;

/// <summary>
/// Provides type-safe access to PDK configuration values.
/// </summary>
public interface IConfiguration
{
    /// <summary>
    /// Gets a string configuration value.
    /// </summary>
    /// <param name="key">The configuration key. Supports nested keys with dot notation (e.g., "docker.memoryLimit").</param>
    /// <param name="defaultValue">The default value if the key is not found.</param>
    /// <returns>The configuration value or the default.</returns>
    string? GetString(string key, string? defaultValue = null);

    /// <summary>
    /// Gets an integer configuration value.
    /// </summary>
    /// <param name="key">The configuration key. Supports nested keys with dot notation.</param>
    /// <param name="defaultValue">The default value if the key is not found or cannot be converted.</param>
    /// <returns>The configuration value or the default.</returns>
    int GetInt(string key, int defaultValue = 0);

    /// <summary>
    /// Gets a boolean configuration value.
    /// </summary>
    /// <param name="key">The configuration key. Supports nested keys with dot notation.</param>
    /// <param name="defaultValue">The default value if the key is not found or cannot be converted.</param>
    /// <returns>The configuration value or the default.</returns>
    bool GetBool(string key, bool defaultValue = false);

    /// <summary>
    /// Gets a double configuration value.
    /// </summary>
    /// <param name="key">The configuration key. Supports nested keys with dot notation.</param>
    /// <param name="defaultValue">The default value if the key is not found or cannot be converted.</param>
    /// <returns>The configuration value or the default.</returns>
    double GetDouble(string key, double defaultValue = 0.0);

    /// <summary>
    /// Gets a configuration section as a typed object.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the section to.</typeparam>
    /// <param name="key">The section key (e.g., "docker", "logging").</param>
    /// <returns>The typed section or null if not found.</returns>
    T? GetSection<T>(string key) where T : class;

    /// <summary>
    /// Tries to get a configuration value.
    /// </summary>
    /// <param name="key">The configuration key. Supports nested keys with dot notation.</param>
    /// <param name="value">The value if found.</param>
    /// <returns>True if the value was found, false otherwise.</returns>
    bool TryGetValue(string key, out object? value);

    /// <summary>
    /// Gets all configuration keys, optionally filtered by section.
    /// </summary>
    /// <param name="section">Optional section to filter keys. If null, returns top-level keys.</param>
    /// <returns>The configuration keys.</returns>
    IEnumerable<string> GetKeys(string? section = null);

    /// <summary>
    /// Gets the underlying configuration object.
    /// </summary>
    /// <returns>The PdkConfig instance.</returns>
    PdkConfig GetConfig();
}
