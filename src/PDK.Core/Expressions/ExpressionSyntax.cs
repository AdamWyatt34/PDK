namespace PDK.Core.Expressions;

/// <summary>
/// The expression dialect to use when parsing and evaluating pipeline expressions.
/// </summary>
public enum ExpressionSyntax
{
    /// <summary>GitHub Actions: <c>${{ github.ref == 'refs/heads/main' &amp;&amp; success() }}</c>.</summary>
    GitHub,

    /// <summary>Azure Pipelines: <c>and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))</c>, plus <c>$(macro)</c> and <c>$[ runtime ]</c> forms.</summary>
    Azure
}

/// <summary>
/// Outcome of the job (or of the steps executed so far) used by the status functions
/// <c>success()</c>, <c>failure()</c>, <c>cancelled()</c>, <c>succeeded()</c>, <c>failed()</c>.
/// </summary>
public enum ExpressionJobStatus
{
    /// <summary>Everything so far succeeded.</summary>
    Success,

    /// <summary>At least one step (or a needed job) failed.</summary>
    Failure,

    /// <summary>The run was cancelled.</summary>
    Cancelled,

    /// <summary>
    /// A job this job depends on was skipped (GitHub semantics): <c>success()</c> and <c>failure()</c>
    /// are both false, so only <c>always()</c>-style conditions run.
    /// </summary>
    Skipped
}
