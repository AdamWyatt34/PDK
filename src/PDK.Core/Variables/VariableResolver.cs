namespace PDK.Core.Variables;

using System.Collections.Concurrent;
using PDK.Core.Configuration;
using PDK.Core.Secrets;

/// <summary>
/// Resolves variable values from multiple sources with precedence ordering.
/// Thread-safe implementation using ConcurrentDictionary.
/// Precedence: CLI > Secrets > Environment > Configuration > BuiltIn.
/// </summary>
public class VariableResolver : IVariableResolver
{
    private readonly IBuiltInVariables _builtInVariables;

    /// <summary>
    /// Prefix for PDK environment variables that should have the prefix stripped.
    /// </summary>
    private const string PdkVarPrefix = "PDK_VAR_";

    /// <summary>
    /// Prefix for PDK secret environment variables that should be treated as secrets.
    /// </summary>
    private const string PdkSecretPrefix = "PDK_SECRET_";

    /// <summary>
    /// Variables stored by source for precedence handling.
    /// Key is variable name, value is (value, source) tuple.
    /// </summary>
    private readonly ConcurrentDictionary<string, (string Value, VariableSource Source)> _variables = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="VariableResolver"/> class.
    /// </summary>
    public VariableResolver()
        : this(new BuiltInVariables())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VariableResolver"/> class.
    /// </summary>
    /// <param name="builtInVariables">The built-in variables provider.</param>
    public VariableResolver(IBuiltInVariables builtInVariables)
    {
        _builtInVariables = builtInVariables ?? throw new ArgumentNullException(nameof(builtInVariables));
    }

    /// <inheritdoc/>
    public string? Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Check stored variables first (CLI, Environment, Configuration)
        if (_variables.TryGetValue(name, out var stored))
        {
            return stored.Value;
        }

        // Fall back to built-in variables
        return _builtInVariables.GetValue(name);
    }

    /// <inheritdoc/>
    public string Resolve(string name, string defaultValue)
    {
        return Resolve(name) ?? defaultValue;
    }

    /// <inheritdoc/>
    public bool ContainsVariable(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return _variables.ContainsKey(name) || _builtInVariables.IsBuiltIn(name);
    }

    /// <inheritdoc/>
    public VariableSource? GetSource(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (_variables.TryGetValue(name, out var stored))
        {
            return stored.Source;
        }

        if (_builtInVariables.IsBuiltIn(name))
        {
            return VariableSource.BuiltIn;
        }

        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetAllVariables()
    {
        var result = new Dictionary<string, string>();

        // Start with built-in variables (lowest precedence)
        foreach (var (name, value) in _builtInVariables.GetAll())
        {
            result[name] = value;
        }

        // Layer on stored variables (already ordered by precedence when stored)
        foreach (var (name, (value, _)) in _variables)
        {
            result[name] = value;
        }

        return result;
    }

    /// <inheritdoc/>
    public void SetVariable(string name, string value, VariableSource source)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Variable name cannot be null or empty", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(value);

        _variables.AddOrUpdate(
            name,
            (value, source),
            (_, existing) =>
            {
                // Only update if new source has higher or equal precedence
                if (source >= existing.Source)
                {
                    return (value, source);
                }
                return existing;
            });
    }

    /// <inheritdoc/>
    public void ClearSource(VariableSource source)
    {
        var keysToRemove = _variables
            .Where(kvp => kvp.Value.Source == source)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _variables.TryRemove(key, out _);
        }
    }

    /// <inheritdoc/>
    public void LoadFromConfiguration(PdkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Variables != null)
        {
            foreach (var (name, value) in config.Variables)
            {
                SetVariable(name, value, VariableSource.Configuration);
            }
        }
    }

    /// <inheritdoc/>
    public void LoadFromEnvironment()
    {
        var envVars = Environment.GetEnvironmentVariables();

        foreach (var key in envVars.Keys)
        {
            if (key == null)
            {
                continue;
            }

            var name = key.ToString();
            var value = envVars[key]?.ToString();

            if (string.IsNullOrEmpty(name) || value == null)
            {
                continue;
            }

            // Handle PDK_SECRET_* prefix - strip it and store as secret
            // This allows passing secrets via environment variables while still
            // having them treated as secrets (masked in output, etc.)
            if (name.StartsWith(PdkSecretPrefix, StringComparison.Ordinal))
            {
                var strippedName = name[PdkSecretPrefix.Length..];
                if (!string.IsNullOrEmpty(strippedName))
                {
                    SetVariable(strippedName, value, VariableSource.Secret);
                }
            }
            // Handle PDK_VAR_* prefix - strip it and store as regular variable
            else if (name.StartsWith(PdkVarPrefix, StringComparison.Ordinal))
            {
                var strippedName = name[PdkVarPrefix.Length..];
                if (!string.IsNullOrEmpty(strippedName))
                {
                    SetVariable(strippedName, value, VariableSource.Environment);
                }
            }
            else
            {
                // Store all environment variables for potential resolution
                SetVariable(name, value, VariableSource.Environment);
            }
        }
    }

    /// <inheritdoc/>
    public async Task LoadSecretsAsync(ISecretManager secretManager)
    {
        ArgumentNullException.ThrowIfNull(secretManager);

        var secrets = await secretManager.GetAllSecretsAsync();

        foreach (var (name, value) in secrets)
        {
            SetVariable(name, value, VariableSource.Secret);
        }
    }

    /// <inheritdoc/>
    public void UpdateContext(VariableContext context)
    {
        _builtInVariables.UpdateContext(context);
    }
}
