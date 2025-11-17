using PDK.Core.Models;

namespace PDK.CLI;

public class PipelineParserFactory
{
    private readonly IEnumerable<IPipelineParser> _parsers;

    public PipelineParserFactory()
    {
        // TODO: Inject parsers via DI when implemented
        _parsers = new List<IPipelineParser>();
    }

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