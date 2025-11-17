namespace PDK.Core.Models;

public interface IPipelineParser
{
    Pipeline Parse(string yamlContent);
    Task<Pipeline> ParseFile(string filePath);
    bool CanParse(string filePath);
}