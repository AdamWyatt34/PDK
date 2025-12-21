namespace PDK.Core.Configuration;

/// <summary>
/// Provides functionality to merge multiple configuration sources.
/// </summary>
public interface IConfigurationMerger
{
    /// <summary>
    /// Merges multiple configurations in order of precedence.
    /// Later configurations override earlier ones.
    /// </summary>
    /// <param name="configs">The configurations to merge, in order of precedence (lowest to highest).</param>
    /// <returns>A new merged configuration.</returns>
    /// <remarks>
    /// Merging rules:
    /// - For scalar values: later non-null values override earlier values
    /// - For dictionaries: keys are merged, later values override earlier for same key
    /// - For nested objects: properties are recursively merged
    /// - Null values do not override non-null values
    /// - Empty strings do override non-empty strings
    /// </remarks>
    PdkConfig Merge(params PdkConfig[] configs);

    /// <summary>
    /// Merges multiple configurations in order of precedence.
    /// Later configurations override earlier ones.
    /// </summary>
    /// <param name="configs">The configurations to merge, in order of precedence (lowest to highest).</param>
    /// <returns>A new merged configuration.</returns>
    PdkConfig Merge(IEnumerable<PdkConfig> configs);
}
