using PDK.Core.Models;

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
    /// <exception cref="NotSupportedException">Thrown when no parser is found for the file.</exception>
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

        if (parser == null)
        {
            throw new NotSupportedException($"No parser found for file: {filePath}");
        }

        return parser;
    }
}