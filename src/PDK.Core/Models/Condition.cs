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
}