namespace PDK.Core.Models;

/// <summary>
/// Defines the contract for parsing pipeline definition files into the common pipeline model.
/// </summary>
/// <remarks>
/// Implementations convert provider-specific pipeline formats (e.g., GitHub Actions YAML,
/// Azure Pipelines YAML) into PDK's common <see cref="Pipeline"/> model for local execution.
/// </remarks>
public interface IPipelineParser
{
    /// <summary>
    /// Parses pipeline content from a YAML string.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <returns>A <see cref="Pipeline"/> representing the parsed workflow.</returns>
    /// <exception cref="PipelineParseException">Thrown when the YAML is invalid or cannot be parsed.</exception>
    Pipeline Parse(string yamlContent);

    /// <summary>
    /// Parses a pipeline definition file asynchronously.
    /// </summary>
    /// <param name="filePath">The path to the pipeline definition file.</param>
    /// <returns>A task containing the parsed <see cref="Pipeline"/>.</returns>
    /// <exception cref="System.IO.FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="PipelineParseException">Thrown when the file cannot be parsed.</exception>
    Task<Pipeline> ParseFile(string filePath);

    /// <summary>
    /// Determines whether this parser can handle the specified file.
    /// </summary>
    /// <param name="filePath">The path to the pipeline file to check.</param>
    /// <returns><c>true</c> if this parser can handle the file; otherwise, <c>false</c>.</returns>
    bool CanParse(string filePath);
}