namespace PDK.Core.Validation;

/// <summary>
/// Represents the complete result of a dry-run validation.
/// </summary>
public record DryRunResult
{
    /// <summary>
    /// Gets whether the pipeline is valid (no errors, warnings are allowed).
    /// </summary>
    public bool IsValid => !Errors.Any(e => e.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets all validation errors (blocking issues).
    /// </summary>
    public IReadOnlyList<DryRunValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Gets all validation warnings (non-blocking issues).
    /// </summary>
    public IReadOnlyList<DryRunValidationError> Warnings { get; init; } = [];

    /// <summary>
    /// Gets the execution plan, if validation succeeded.
    /// </summary>
    public ExecutionPlan? ExecutionPlan { get; init; }

    /// <summary>
    /// Gets the time taken to perform validation.
    /// </summary>
    public TimeSpan ValidationDuration { get; init; }

    /// <summary>
    /// Gets results from individual validation phases.
    /// </summary>
    public IReadOnlyDictionary<string, PhaseResult> PhaseResults { get; init; }
        = new Dictionary<string, PhaseResult>();

    /// <summary>
    /// Gets all issues (errors and warnings) combined.
    /// </summary>
    public IEnumerable<DryRunValidationError> AllIssues => Errors.Concat(Warnings);

    /// <summary>
    /// Gets errors grouped by category.
    /// </summary>
    public IReadOnlyDictionary<ValidationCategory, IReadOnlyList<DryRunValidationError>> ErrorsByCategory =>
        Errors.GroupBy(e => e.Category)
              .ToDictionary(g => g.Key, g => (IReadOnlyList<DryRunValidationError>)g.ToList());

    /// <summary>
    /// Creates a successful result with an execution plan.
    /// </summary>
    public static DryRunResult Success(
        ExecutionPlan plan,
        TimeSpan duration,
        IEnumerable<DryRunValidationError>? warnings = null,
        IDictionary<string, PhaseResult>? phaseResults = null)
    {
        return new DryRunResult
        {
            ExecutionPlan = plan,
            ValidationDuration = duration,
            Warnings = (warnings?.ToList() ?? []).AsReadOnly(),
            PhaseResults = phaseResults != null
                ? new Dictionary<string, PhaseResult>(phaseResults)
                : new Dictionary<string, PhaseResult>()
        };
    }

    /// <summary>
    /// Creates a failed result with errors.
    /// </summary>
    public static DryRunResult Failure(
        IEnumerable<DryRunValidationError> errors,
        TimeSpan duration,
        IEnumerable<DryRunValidationError>? warnings = null,
        IDictionary<string, PhaseResult>? phaseResults = null)
    {
        var errorList = errors.ToList();
        var warningList = warnings?.ToList() ?? [];

        return new DryRunResult
        {
            Errors = errorList.Where(e => e.Severity == ValidationSeverity.Error).ToList().AsReadOnly(),
            Warnings = errorList.Where(e => e.Severity == ValidationSeverity.Warning)
                                .Concat(warningList)
                                .ToList()
                                .AsReadOnly(),
            ValidationDuration = duration,
            PhaseResults = phaseResults != null
                ? new Dictionary<string, PhaseResult>(phaseResults)
                : new Dictionary<string, PhaseResult>()
        };
    }
}

/// <summary>
/// Represents the result of a single validation phase.
/// </summary>
public record PhaseResult
{
    /// <summary>
    /// Gets the name of the validation phase.
    /// </summary>
    public string PhaseName { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether this phase passed (no errors).
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Gets the time taken for this phase.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the number of errors found in this phase.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Gets the number of warnings found in this phase.
    /// </summary>
    public int WarningCount { get; init; }
}
