namespace PDK.Core.Models;

public class RunContext
{
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public Dictionary<string, string> Variables { get; set; } = new();
    public Dictionary<string, string> Secrets { get; set; } = new();
    public string ArtifactsDirectory { get; set; } = "./.pdk/artifacts";
    public bool UseDocker { get; set; } = true;
    public string? SpecificJob { get; set; }
    public string? SpecificStep { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}