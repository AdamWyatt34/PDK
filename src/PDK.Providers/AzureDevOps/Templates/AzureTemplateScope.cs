using System.Collections;
using PDK.Core.Expressions;

namespace PDK.Providers.AzureDevOps.Templates;

/// <summary>
/// What a template expression can see at one point of the document: the resolved parameters of the file being
/// expanded, the compile-time variables defined so far, and the loop variables of the enclosing
/// <c>${{ each }}</c> directives.
/// </summary>
internal sealed class AzureTemplateScope
{
    public AzureTemplateScope(
        string file,
        IReadOnlyDictionary<string, object?> parameters,
        CompileTimeVariables variables,
        IReadOnlyDictionary<string, object?>? loopVariables)
    {
        File = file;
        Parameters = parameters;
        Variables = variables;
        LoopVariables = loopVariables;
    }

    /// <summary>Gets the file whose expressions are evaluated in this scope.</summary>
    public string File { get; }

    /// <summary>Gets the resolved parameters (case-insensitive names).</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    /// <summary>Gets the compile-time variables.</summary>
    public CompileTimeVariables Variables { get; }

    /// <summary>Gets the loop variables, or null outside <c>${{ each }}</c> bodies.</summary>
    public IReadOnlyDictionary<string, object?>? LoopVariables { get; }

    /// <summary>Whether <paramref name="name"/> is a loop variable of an enclosing <c>${{ each }}</c>.</summary>
    public bool IsLoopVariable(string name) => LoopVariables?.ContainsKey(name) == true;

    /// <summary>Builds the expression context for this scope.</summary>
    public ExpressionContext CreateContext(string? workspace)
    {
        var context = new ExpressionContext(ExpressionSyntax.Azure) { Workspace = workspace };
        context.SetRoot("parameters", Parameters);
        context.SetRoot("variables", Variables);

        if (LoopVariables is not null)
        {
            foreach (var (name, value) in LoopVariables)
            {
                context.SetRoot(name, value);
            }
        }

        return context;
    }

    /// <summary>Creates a scope whose variables layer on top of this one (stage and job mappings).</summary>
    public AzureTemplateScope CreateChild() => new(File, Parameters, new CompileTimeVariables(Variables), LoopVariables);

    /// <summary>Creates a scope with an additional loop variable.</summary>
    public AzureTemplateScope WithLoopVariable(string name, object? value)
    {
        var loops = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (LoopVariables is not null)
        {
            foreach (var (existing, existingValue) in LoopVariables)
            {
                loops[existing] = existingValue;
            }
        }

        loops[name] = value;
        return new AzureTemplateScope(File, Parameters, Variables, loops);
    }

    /// <summary>Creates the scope of an included template: its own parameters, the caller's variables, no loop variables.</summary>
    public AzureTemplateScope ForTemplate(string file, IReadOnlyDictionary<string, object?> parameters) =>
        new(file, parameters, new CompileTimeVariables(Variables), null);
}

/// <summary>
/// The <c>variables</c> context of template expressions: a chain of layers (pipeline, stage, job) over the
/// <c>--var</c> values and the predefined <c>Build.*</c>/<c>System.*</c> variables, which are computed lazily
/// because they need git metadata.
/// </summary>
internal sealed class CompileTimeVariables : IReadOnlyDictionary<string, object?>
{
    private readonly CompileTimeVariables? _parent;
    private readonly Lazy<IReadOnlyDictionary<string, string>>? _predefined;
    private readonly Dictionary<string, object?> _local = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the root layer.</summary>
    public CompileTimeVariables(Lazy<IReadOnlyDictionary<string, string>> predefined, IReadOnlyDictionary<string, string>? initial)
    {
        _predefined = predefined;
        if (initial is not null)
        {
            foreach (var (name, value) in initial)
            {
                _local[name] = value;
            }
        }
    }

    /// <summary>Creates a layer on top of <paramref name="parent"/>.</summary>
    public CompileTimeVariables(CompileTimeVariables parent)
    {
        _parent = parent;
    }

    /// <summary>Defines (or redefines) a variable in this layer.</summary>
    public void Set(string name, object? value) => _local[name] = value;

    /// <inheritdoc />
    public object? this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

    /// <inheritdoc />
    public IEnumerable<string> Keys => Snapshot().Keys;

    /// <inheritdoc />
    public IEnumerable<object?> Values => Snapshot().Values;

    /// <inheritdoc />
    public int Count => Snapshot().Count;

    /// <inheritdoc />
    public bool ContainsKey(string key) => TryGetValue(key, out _);

    /// <inheritdoc />
    public bool TryGetValue(string key, out object? value)
    {
        if (_local.TryGetValue(key, out value))
        {
            return true;
        }

        if (_parent is not null)
        {
            return _parent.TryGetValue(key, out value);
        }

        if (_predefined is not null && _predefined.Value.TryGetValue(key, out var predefined))
        {
            value = predefined;
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private Dictionary<string, object?> Snapshot()
    {
        Dictionary<string, object?> result;
        if (_parent is not null)
        {
            result = _parent.Snapshot();
        }
        else
        {
            result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (_predefined is not null)
            {
                foreach (var (name, value) in _predefined.Value)
                {
                    result[name] = value;
                }
            }
        }

        foreach (var (name, value) in _local)
        {
            result[name] = value;
        }

        return result;
    }
}
