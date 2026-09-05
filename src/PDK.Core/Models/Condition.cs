namespace PDK.Core.Models;

/// <summary>
/// Represents a conditional expression that controls job or step execution.
/// </summary>
/// <remarks>
/// Conditions allow jobs and steps to run only when certain criteria are met,
/// such as checking the status of previous steps or evaluating expressions.
/// </remarks>
public class Condition
{
    /// <summary>
    /// Gets or sets the condition expression to evaluate.
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of condition (e.g., Always, Success, Failure).
    /// </summary>
    public ConditionType Type { get; set; }

    /// <summary>
    /// Gets or sets an optional human-readable explanation of the condition, used in skip reasons instead of
    /// <see cref="Expression"/> when present. Parsers that decide at parse time that a job does not run
    /// (GitLab <c>rules</c>, <c>only</c>/<c>except</c>, <c>when: manual</c>, <c>workflow:rules</c>) set
    /// <see cref="Expression"/> to <c>false</c> and describe the reason here.
    /// </summary>
    public string? Description { get; set; }
}