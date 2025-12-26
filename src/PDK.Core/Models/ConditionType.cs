namespace PDK.Core.Models;

/// <summary>
/// Specifies the type of condition for job or step execution.
/// </summary>
public enum ConditionType
{
    /// <summary>
    /// Always execute regardless of previous step outcomes.
    /// </summary>
    Always,

    /// <summary>
    /// Execute only if all previous steps succeeded.
    /// </summary>
    Success,

    /// <summary>
    /// Execute only if a previous step failed.
    /// </summary>
    Failure,

    /// <summary>
    /// Evaluate a custom expression to determine execution.
    /// </summary>
    Expression
}