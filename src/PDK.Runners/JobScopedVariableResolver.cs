using PDK.Core.Configuration;
using PDK.Core.Secrets;
using PDK.Core.Variables;

namespace PDK.Runners;

/// <summary>
/// A per-job view over the shared <see cref="IVariableResolver"/>: the PDK built-ins that describe the
/// current job (<c>PDK_WORKSPACE</c>, <c>PDK_RUNNER</c>, <c>PDK_JOB</c>, <c>PDK_STEP</c>) are answered from
/// this instance instead of mutating shared state, so jobs can run concurrently.
/// </summary>
public sealed class JobScopedVariableResolver : IVariableResolver
{
    private readonly IVariableResolver _inner;

    /// <summary>Creates a scoped resolver for one job.</summary>
    /// <param name="inner">The shared resolver.</param>
    /// <param name="workspace">The job's workspace path.</param>
    /// <param name="runner">The runner name (host, docker, or the runner label).</param>
    /// <param name="jobName">The job name.</param>
    public JobScopedVariableResolver(IVariableResolver inner, string workspace, string runner, string jobName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Workspace = workspace;
        Runner = runner;
        JobName = jobName;
    }

    /// <summary>Gets the workspace path reported as <c>PDK_WORKSPACE</c>.</summary>
    public string Workspace { get; }

    /// <summary>Gets the runner reported as <c>PDK_RUNNER</c>.</summary>
    public string Runner { get; }

    /// <summary>Gets the job name reported as <c>PDK_JOB</c>.</summary>
    public string JobName { get; }

    /// <summary>Gets or sets the step currently executing, reported as <c>PDK_STEP</c>.</summary>
    public string? StepName { get; set; }

    /// <inheritdoc/>
    public string? Resolve(string name) => TryScoped(name, out var value) ? value : _inner.Resolve(name);

    /// <inheritdoc/>
    public string Resolve(string name, string defaultValue) => Resolve(name) ?? defaultValue;

    /// <inheritdoc/>
    public bool ContainsVariable(string name) => TryScoped(name, out _) || _inner.ContainsVariable(name);

    /// <inheritdoc/>
    public VariableSource? GetSource(string name) => TryScoped(name, out _) ? VariableSource.BuiltIn : _inner.GetSource(name);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetAllVariables()
    {
        var all = new Dictionary<string, string>(_inner.GetAllVariables());
        all["PDK_WORKSPACE"] = Workspace;
        all["PDK_RUNNER"] = Runner;
        all["PDK_JOB"] = JobName;
        if (StepName != null)
        {
            all["PDK_STEP"] = StepName;
        }

        return all;
    }

    /// <inheritdoc/>
    public void SetVariable(string name, string value, VariableSource source) => _inner.SetVariable(name, value, source);

    /// <inheritdoc/>
    public void ClearSource(VariableSource source) => _inner.ClearSource(source);

    /// <inheritdoc/>
    public void LoadFromConfiguration(PdkConfig config) => _inner.LoadFromConfiguration(config);

    /// <inheritdoc/>
    public void LoadFromEnvironment() => _inner.LoadFromEnvironment();

    /// <inheritdoc/>
    public Task LoadSecretsAsync(ISecretManager secretManager) => _inner.LoadSecretsAsync(secretManager);

    /// <inheritdoc/>
    public void UpdateContext(VariableContext context)
    {
        // The shared context is deliberately left alone; per-job values live on this instance.
        ArgumentNullException.ThrowIfNull(context);
        StepName = context.StepName ?? StepName;
    }

    private bool TryScoped(string name, out string? value)
    {
        switch (name)
        {
            case "PDK_WORKSPACE":
                value = Workspace;
                return true;
            case "PDK_RUNNER":
                value = Runner;
                return true;
            case "PDK_JOB":
                value = JobName;
                return true;
            case "PDK_STEP":
                value = StepName ?? string.Empty;
                return true;
            default:
                value = null;
                return false;
        }
    }
}
