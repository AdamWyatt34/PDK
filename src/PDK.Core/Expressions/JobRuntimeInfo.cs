using PDK.Core.Models;

namespace PDK.Core.Expressions;

/// <summary>
/// Everything about the current run that expressions and the exported environment need
/// but that is not part of the pipeline model itself.
/// </summary>
public sealed record JobRuntimeInfo
{
    /// <summary>Host workspace path.</summary>
    public required string Workspace { get; init; }

    /// <summary>Workspace path as seen by the steps (container path in Docker mode).</summary>
    public string? StepWorkspace { get; init; }

    /// <summary>Temp directory as seen by the steps.</summary>
    public string? StepTempDirectory { get; init; }

    /// <summary>Pipeline provider (selects the expression dialect and context shape).</summary>
    public required PipelineProvider Provider { get; init; }

    /// <summary>Pipeline (workflow) name.</summary>
    public string PipelineName { get; init; } = string.Empty;

    /// <summary>Runner OS as GitHub reports it: Linux, Windows or macOS.</summary>
    public string RunnerOs { get; init; } = DetectOs();

    /// <summary>Runner architecture as GitHub reports it: X64, ARM64, X86, ARM.</summary>
    public string RunnerArch { get; init; } = DetectArch();

    /// <summary>Secrets available to the run (name → value).</summary>
    public IReadOnlyDictionary<string, string> Secrets { get; init; } = new Dictionary<string, string>();

    /// <summary>Configuration / CLI variables (GitHub <c>vars</c> context, Azure variables).</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();

    /// <summary>Workflow inputs (<c>inputs</c> context).</summary>
    public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>();

    /// <summary>Results of jobs this job depends on (job id → success | failure | skipped | cancelled).</summary>
    public IReadOnlyDictionary<string, string> NeedsResults { get; init; } = new Dictionary<string, string>();

    /// <summary>Outputs of jobs this job depends on (job id → output name → value).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NeedsOutputs { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>Event that "triggered" the run (default <c>push</c>).</summary>
    public string EventName { get; init; } = "push";

    /// <summary>Run identifier.</summary>
    public string RunId { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Run number.</summary>
    public int RunNumber { get; init; } = 1;

    /// <summary>Git metadata for the workspace.</summary>
    public GitInfo Git { get; init; } = GitInfo.Empty;

    /// <summary>Container image the job runs in (Docker mode), or null.</summary>
    public string? ContainerImage { get; init; }

    /// <summary>Actor name (defaults to the current user).</summary>
    public string Actor { get; init; } = Environment.UserName;

    private static string DetectOs()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        return "Linux";
    }

    private static string DetectArch() => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
        System.Runtime.InteropServices.Architecture.Arm => "ARM",
        System.Runtime.InteropServices.Architecture.X86 => "X86",
        _ => "X64"
    };
}

/// <summary>Outcome of a completed step, used for the <c>steps.&lt;id&gt;</c> context.</summary>
/// <param name="Id">Step id (null when the step has none).</param>
/// <param name="Outcome"><c>success</c>, <c>failure</c>, <c>cancelled</c> or <c>skipped</c> before continue-on-error is applied.</param>
/// <param name="Conclusion">Outcome after continue-on-error is applied.</param>
/// <param name="Outputs">Outputs written to <c>$GITHUB_OUTPUT</c> / <c>##vso[task.setvariable;isOutput=true]</c>.</param>
public sealed record StepOutcome(string? Id, string Outcome, string Conclusion, IReadOnlyDictionary<string, string> Outputs);
