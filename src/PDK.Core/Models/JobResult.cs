namespace PDK.Core.Models;

public class JobResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
    public List<StepResult> StepResults { get; set; } = [];

    public static JobResult Succeeded() => new() { Success = true };
    public static JobResult Failed(string error) => new() { Success = false, Error = error };
}