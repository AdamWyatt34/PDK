namespace PDK.Core.Variables;

/// <summary>
/// Provides context information for variable resolution during pipeline execution.
/// Used to populate built-in variables like PDK_WORKSPACE, PDK_JOB, PDK_STEP, etc.
/// </summary>
public record VariableContext
{
    /// <summary>
    /// Gets or initializes the workspace directory path.
    /// Used to populate the PDK_WORKSPACE built-in variable.
    /// </summary>
    public string? Workspace { get; init; }

    /// <summary>
    /// Gets or initializes the runner identifier.
    /// Used to populate the PDK_RUNNER built-in variable.
    /// </summary>
    public string? Runner { get; init; }

    /// <summary>
    /// Gets or initializes the current job name.
    /// Used to populate the PDK_JOB built-in variable.
    /// </summary>
    public string? JobName { get; init; }

    /// <summary>
    /// Gets or initializes the current step name.
    /// Used to populate the PDK_STEP built-in variable.
    /// </summary>
    public string? StepName { get; init; }

    /// <summary>
    /// Creates a default context with current directory as workspace.
    /// </summary>
    /// <returns>A new VariableContext with default values.</returns>
    public static VariableContext CreateDefault()
    {
        return new VariableContext
        {
            Workspace = Environment.CurrentDirectory,
            Runner = "local"
        };
    }

    /// <summary>
    /// Creates a new context with the specified job name.
    /// </summary>
    /// <param name="jobName">The job name.</param>
    /// <returns>A new VariableContext with the job name set.</returns>
    public VariableContext WithJob(string jobName)
    {
        return this with { JobName = jobName };
    }

    /// <summary>
    /// Creates a new context with the specified step name.
    /// </summary>
    /// <param name="stepName">The step name.</param>
    /// <returns>A new VariableContext with the step name set.</returns>
    public VariableContext WithStep(string stepName)
    {
        return this with { StepName = stepName };
    }
}
