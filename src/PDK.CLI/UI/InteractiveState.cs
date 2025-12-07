namespace PDK.CLI.UI;

using PDK.Core.Models;

/// <summary>
/// States for the interactive mode state machine (TS-06-004).
/// </summary>
public enum InteractiveState
{
    /// <summary>Top-level main menu.</summary>
    MainMenu,

    /// <summary>Selecting job(s) to run.</summary>
    JobSelection,

    /// <summary>Viewing detailed job information.</summary>
    JobDetails,

    /// <summary>Executing job(s).</summary>
    JobExecution,

    /// <summary>After job execution completes.</summary>
    ExecutionComplete,

    /// <summary>Exiting interactive mode.</summary>
    Exit
}

/// <summary>
/// Context for interactive mode state machine.
/// Holds all state between transitions (TS-06-004).
/// </summary>
public sealed class InteractiveContext
{
    /// <summary>
    /// Gets or sets the parsed pipeline.
    /// </summary>
    public Pipeline Pipeline { get; set; } = null!;

    /// <summary>
    /// Gets or sets the path to the pipeline file.
    /// </summary>
    public string PipelineFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets the list of jobs selected for execution.
    /// </summary>
    public List<Job> SelectedJobs { get; } = [];

    /// <summary>
    /// Gets or sets the current job being viewed in details.
    /// </summary>
    public Job? CurrentJob { get; set; }

    /// <summary>
    /// Gets the results from job executions.
    /// </summary>
    public List<PDK.Runners.JobExecutionResult> ExecutionResults { get; } = [];

    /// <summary>
    /// Gets or sets whether verbose mode is enabled.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Gets or sets the error message if an error occurred.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Resets the context for a new operation while keeping pipeline data.
    /// </summary>
    public void Reset()
    {
        SelectedJobs.Clear();
        CurrentJob = null;
        ExecutionResults.Clear();
        ErrorMessage = null;
        Verbose = false;
    }
}
