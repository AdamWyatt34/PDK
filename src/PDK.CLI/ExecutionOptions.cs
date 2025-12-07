/// <summary>
/// Options for pipeline execution.
/// </summary>
public class ExecutionOptions
{
    /// <summary>
    /// Gets or sets the path to the pipeline file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the specific job name to run. If null, all jobs are run.
    /// </summary>
    public string? JobName { get; set; }

    /// <summary>
    /// Gets or sets the specific step name to run within a job.
    /// </summary>
    public string? StepName { get; set; }

    /// <summary>
    /// Gets or sets whether to use Docker for execution. Default is true.
    /// </summary>
    public bool UseDocker { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to only validate the pipeline without executing.
    /// </summary>
    public bool ValidateOnly { get; set; }

    /// <summary>
    /// Gets or sets whether to enable verbose output logging.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Gets or sets whether to suppress step output (quiet mode).
    /// When true, only job/step status is shown without detailed output.
    /// </summary>
    public bool Quiet { get; set; }
}