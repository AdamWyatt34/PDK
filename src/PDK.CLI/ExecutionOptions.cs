using PDK.Core.Runners;

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
    /// Gets or sets the requested runner type for execution.
    /// Default is Auto, which prefers Docker but falls back to Host if unavailable.
    /// </summary>
    public RunnerType RunnerType { get; set; } = RunnerType.Auto;

    /// <summary>
    /// Gets or sets whether to use Docker for execution.
    /// This property is maintained for backward compatibility.
    /// Use RunnerType for explicit runner control.
    /// </summary>
    [Obsolete("Use RunnerType instead. This property is maintained for backward compatibility.")]
    public bool UseDocker
    {
        get => RunnerType != RunnerType.Host;
        set => RunnerType = value ? RunnerType.Auto : RunnerType.Host;
    }

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

    /// <summary>
    /// Gets or sets whether to disable container reuse within a job.
    /// When true, a new container is created for each step.
    /// Default is false (containers are reused).
    /// </summary>
    public bool NoReuseContainers { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to disable Docker image caching.
    /// When true, images are always pulled from the registry.
    /// Default is false (images are cached).
    /// </summary>
    public bool NoCacheImages { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable parallel step execution.
    /// When true, steps without dependencies can run concurrently.
    /// Default is false (sequential execution).
    /// </summary>
    public bool ParallelSteps { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of steps to run in parallel.
    /// Only applies when ParallelSteps is true.
    /// Default is 4.
    /// </summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>
    /// Gets or sets whether to show performance metrics after execution.
    /// Also enabled when Verbose is true.
    /// </summary>
    public bool ShowMetrics { get; set; } = false;

    /// <summary>
    /// Gets or sets whether watch mode is enabled (REQ-11-001.1).
    /// When true, the pipeline will be re-executed automatically when files change.
    /// </summary>
    public bool WatchMode { get; set; } = false;

    /// <summary>
    /// Gets or sets the watch mode debounce period in milliseconds.
    /// Default is 500ms (REQ-11-001.4).
    /// </summary>
    public int WatchDebounceMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets whether to clear the terminal between watch mode runs.
    /// Default is false (REQ-11-002.4).
    /// </summary>
    public bool WatchClear { get; set; } = false;
}