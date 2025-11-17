namespace PDK.Core.Models;

public class StepResult
{
    public string StepName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
}