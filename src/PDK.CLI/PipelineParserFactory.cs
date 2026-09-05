using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers.Common;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PDK.CLI;

/// <summary>
/// Interface for selecting the appropriate pipeline parser based on file type.
/// </summary>
public interface IPipelineParserFactory
{
    /// <summary>
    /// Gets the appropriate parser for the given file.
    /// </summary>
    /// <param name="filePath">Path to the pipeline file.</param>
    /// <returns>A parser that can handle the file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="PipelineParseException">
    /// Thrown when the file is not valid YAML or is neither a GitHub Actions workflow nor an Azure DevOps pipeline.
    /// </exception>
    IPipelineParser GetParser(string filePath);
}

/// <summary>
/// Factory for selecting the appropriate pipeline parser based on file type.
/// </summary>
public class PipelineParserFactory : IPipelineParserFactory
{
    private readonly IEnumerable<IPipelineParser> _parsers;

    /// <summary>
    /// Initializes a new instance of PipelineParserFactory with the provided parsers.
    /// </summary>
    /// <param name="parsers">Available pipeline parsers.</param>
    public PipelineParserFactory(IEnumerable<IPipelineParser> parsers)
    {
        _parsers = parsers ?? throw new ArgumentNullException(nameof(parsers));
    }

    /// <inheritdoc/>
    public IPipelineParser GetParser(string filePath)
    {
        var parser = _parsers.FirstOrDefault(p => p.CanParse(filePath));
        if (parser != null)
        {
            return parser;
        }

        throw DescribeUnparsableFile(filePath);
    }

    /// <summary>
    /// Explains why no parser accepted the file: it is missing, it is not valid YAML (with the line and column),
    /// or it does not look like a pipeline of a supported provider.
    /// </summary>
    private static Exception DescribeUnparsableFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new FileNotFoundException($"Pipeline file not found: {filePath}", filePath);
        }

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PipelineParseException(
                ErrorCodes.FileAccessDenied,
                $"Pipeline file could not be read: {filePath} ({ex.Message})",
                ErrorContext.FromParserPosition(filePath, 0, 0),
                ["Check the file permissions"],
                ex);
        }

        try
        {
            using var reader = new StringReader(content);
            new YamlStream().Load(reader);
        }
        catch (YamlException ex)
        {
            return YamlErrorTranslator.Translate(ex, content, filePath);
        }

        return new PipelineParseException(
            ErrorCodes.UnknownProvider,
            $"{Path.GetFileName(filePath)} is not a GitHub Actions workflow or an Azure DevOps pipeline",
            ErrorContext.FromParserPosition(filePath, 0, 0),
            [
                "A GitHub Actions workflow needs a top-level 'jobs:' mapping and an 'on:' trigger (or jobs with 'runs-on')",
                "An Azure DevOps pipeline needs top-level 'steps:', 'jobs:', 'stages:', 'pool:' or 'trigger:'",
                "Use --file to point at the pipeline definition if PDK picked up the wrong file"
            ]);
    }
}
