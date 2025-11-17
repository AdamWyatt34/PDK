namespace PDK.Core.Models;

public class Step
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public StepType Type { get; set; }
    public string? Script { get; set; }
    public string Shell { get; set; } = "bash";
    public Dictionary<string, string> With { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new();
    public bool ContinueOnError { get; set; }
    public Condition? Condition { get; set; }
    public string? WorkingDirectory { get; set; }
}