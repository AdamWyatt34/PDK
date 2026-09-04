using PDK.Runners;

namespace PDK.CLI;

/// <summary>
/// Outcome of a <see cref="PipelineExecutor.Execute"/> call.
/// </summary>
public sealed record PipelineRunResult
{
    /// <summary>Gets whether the pipeline (or the selected jobs) completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the process exit code that represents this outcome.</summary>
    public int ExitCode { get; init; }

    /// <summary>Gets the per-job results, in execution order. Empty for validate-only and preview runs.</summary>
    public IReadOnlyList<JobExecutionResult> JobResults { get; init; } = Array.Empty<JobExecutionResult>();

    /// <summary>Gets an optional message describing the outcome (used for failures that are not job failures).</summary>
    public string? Message { get; init; }

    /// <summary>Creates a successful result.</summary>
    public static PipelineRunResult Succeeded(IReadOnlyList<JobExecutionResult>? jobs = null) =>
        new() { Success = true, ExitCode = ExitCodes.Success, JobResults = jobs ?? Array.Empty<JobExecutionResult>() };

    /// <summary>Creates a failed result.</summary>
    public static PipelineRunResult Failed(int exitCode, string? message = null, IReadOnlyList<JobExecutionResult>? jobs = null) =>
        new() { Success = false, ExitCode = exitCode, Message = message, JobResults = jobs ?? Array.Empty<JobExecutionResult>() };
}
