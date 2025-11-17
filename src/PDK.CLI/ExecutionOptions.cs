public class ExecutionOptions
{
    public string FilePath { get; set; } = string.Empty;
    public string? JobName { get; set; }
    public string? StepName { get; set; }
    public bool UseDocker { get; set; } = true;
    public bool ValidateOnly { get; set; }
    public bool Verbose { get; set; }
}