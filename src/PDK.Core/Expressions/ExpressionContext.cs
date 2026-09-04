namespace PDK.Core.Expressions;

/// <summary>
/// The data an expression is evaluated against: named roots (<c>github</c>, <c>env</c>, <c>secrets</c>,
/// <c>matrix</c>, <c>steps</c>, <c>needs</c>, <c>variables</c>, ...), the current job status used by the
/// status functions, and a macro resolver for Azure <c>$(name)</c> references.
/// </summary>
public sealed class ExpressionContext
{
    private readonly Dictionary<string, object?> _roots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a context for the given dialect.</summary>
    public ExpressionContext(ExpressionSyntax syntax)
    {
        Syntax = syntax;
    }

    /// <summary>Gets the dialect.</summary>
    public ExpressionSyntax Syntax { get; }

    /// <summary>Gets or sets the workspace directory (used by <c>hashFiles()</c>).</summary>
    public string? Workspace { get; set; }

    /// <summary>Gets or sets the status used by <c>success()</c> / <c>failure()</c> / <c>succeeded()</c> / <c>failed()</c>.</summary>
    public ExpressionJobStatus Status { get; set; } = ExpressionJobStatus.Success;

    /// <summary>Gets the root names that are defined.</summary>
    public IEnumerable<string> RootNames => _roots.Keys;

    /// <summary>Defines (or replaces) a root value such as <c>github</c> or <c>env</c>.</summary>
    public ExpressionContext SetRoot(string name, object? value)
    {
        _roots[name] = value;
        return this;
    }

    /// <summary>Gets a root value, or null when it is not defined.</summary>
    public object? GetRoot(string name) => _roots.TryGetValue(name, out var v) ? v : null;

    /// <summary>True when a root with this name is defined.</summary>
    public bool HasRoot(string name) => _roots.ContainsKey(name);

    /// <summary>
    /// Resolves an Azure <c>$(name)</c> macro. Returns null when the name is unknown so the
    /// macro is left untouched in the text, matching Azure Pipelines behaviour.
    /// </summary>
    public string? ResolveMacro(string name)
    {
        var variables = GetRoot("variables");
        var value = ExpressionValue.GetProperty(variables, name);
        return value == null ? null : ExpressionValue.ToText(value);
    }

    /// <summary>Returns a shallow copy that shares nothing with this instance (roots are copied by reference).</summary>
    public ExpressionContext Clone()
    {
        var copy = new ExpressionContext(Syntax) { Workspace = Workspace, Status = Status };
        foreach (var (k, v) in _roots)
        {
            copy._roots[k] = v;
        }

        return copy;
    }
}
