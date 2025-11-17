namespace PDK.Core.Models;

public class Pipeline
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, Job> Jobs { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = new();
    public string? DefaultBranch { get; set; }
    public PipelineProvider Provider { get; set; }
}