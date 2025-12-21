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

    /// <summary>
    /// Gets or sets the explicit path to a configuration file.
    /// If null, configuration is auto-discovered using standard search order.
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Gets or sets CLI-provided variables (from --var NAME=VALUE).
    /// These have highest precedence and override all other sources.
    /// </summary>
    public Dictionary<string, string> CliVariables { get; set; } = new();

    /// <summary>
    /// Gets or sets the path to a JSON file containing variables.
    /// Variables from this file are treated as configuration-level precedence.
    /// </summary>
    public string? VarFilePath { get; set; }

    /// <summary>
    /// Gets or sets CLI-provided secrets (from --secret NAME=VALUE).
    /// WARNING: Using --secret exposes values in process list.
    /// These values are automatically registered for masking in output.
    /// </summary>
    public Dictionary<string, string> CliSecrets { get; set; } = new();
}