namespace PDK.Core.Models;

public class Job
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RunsOn { get; set; } = "ubuntu-latest";
    public List<Step> Steps { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = new();
    public List<string> DependsOn { get; set; } = [];
    public Condition? Condition { get; set; }
    public TimeSpan? Timeout { get; set; }
}