namespace PDK.Core.Configuration;

using System.Collections.Concurrent;
using System.Reflection;

/// <summary>
/// Provides type-safe access to PDK configuration values.
/// Thread-safe implementation of <see cref="IConfiguration"/>.
/// </summary>
public class PdkConfiguration : IConfiguration
{
    private readonly PdkConfig _config;
    private readonly object _lock = new();

    /// <summary>
    /// Cache for property accessors to improve nested key navigation performance.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PdkConfiguration"/> class.
    /// </summary>
    /// <param name="config">The configuration to wrap.</param>
    public PdkConfiguration(PdkConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public string? GetString(string key, string? defaultValue = null)
    {
        lock (_lock)
        {
            if (TryGetValueInternal(key, out var value))
            {
                return value?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }
    }

    /// <inheritdoc/>
    public int GetInt(string key, int defaultValue = 0)
    {
        lock (_lock)
        {
            if (TryGetValueInternal(key, out var value))
            {
                return value switch
                {
                    int i => i,
                    long l => (int)l,
                    double d => (int)d,
                    string s when int.TryParse(s, out var parsed) => parsed,
                    _ => defaultValue
                };
            }
            return defaultValue;
        }
    }

    /// <inheritdoc/>
    public bool GetBool(string key, bool defaultValue = false)
    {
        lock (_lock)
        {
            if (TryGetValueInternal(key, out var value))
            {
                return value switch
                {
                    bool b => b,
                    string s when bool.TryParse(s, out var parsed) => parsed,
                    string s when s.Equals("1", StringComparison.Ordinal) => true,
                    string s when s.Equals("0", StringComparison.Ordinal) => false,
                    int i => i != 0,
                    _ => defaultValue
                };
            }
            return defaultValue;
        }
    }

    /// <inheritdoc/>
    public double GetDouble(string key, double defaultValue = 0.0)
    {
        lock (_lock)
        {
            if (TryGetValueInternal(key, out var value))
            {
                return value switch
                {
                    double d => d,
                    float f => f,
                    int i => i,
                    long l => l,
                    string s when double.TryParse(s, out var parsed) => parsed,
                    _ => defaultValue
                };
            }
            return defaultValue;
        }
    }

    /// <inheritdoc/>
    public T? GetSection<T>(string key) where T : class
    {
        lock (_lock)
        {
            if (TryGetValueInternal(key, out var value) && value is T typed)
            {
                return typed;
            }
            return null;
        }
    }

    /// <inheritdoc/>
    public bool TryGetValue(string key, out object? value)
    {
        lock (_lock)
        {
            return TryGetValueInternal(key, out value);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetKeys(string? section = null)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(section))
            {
                // Return top-level keys
                return GetTopLevelKeys();
            }

            // Get keys for a specific section
            if (TryGetValueInternal(section, out var sectionValue) && sectionValue != null)
            {
                return GetKeysForObject(sectionValue);
            }

            return [];
        }
    }

    /// <inheritdoc/>
    public PdkConfig GetConfig()
    {
        return _config;
    }

    private bool TryGetValueInternal(string key, out object? value)
    {
        value = null;

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var parts = key.Split('.');
        object? current = _config;

        foreach (var part in parts)
        {
            if (current == null)
            {
                return false;
            }

            // Handle dictionary access for variables and secrets
            if (current is Dictionary<string, string> dict)
            {
                if (dict.TryGetValue(part, out var dictValue))
                {
                    current = dictValue;
                    continue;
                }
                return false;
            }

            // Handle property access
            var type = current.GetType();
            var properties = GetCachedProperties(type);
            var property = FindProperty(properties, part);

            if (property == null)
            {
                return false;
            }

            current = property.GetValue(current);
        }

        value = current;
        return true;
    }

    private static PropertyInfo[] GetCachedProperties(Type type)
    {
        return PropertyCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    private static PropertyInfo? FindProperty(PropertyInfo[] properties, string name)
    {
        // Try exact match first (case-sensitive)
        foreach (var prop in properties)
        {
            if (prop.Name.Equals(name, StringComparison.Ordinal))
            {
                return prop;
            }
        }

        // Try case-insensitive match
        foreach (var prop in properties)
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return prop;
            }
        }

        return null;
    }

    private IEnumerable<string> GetTopLevelKeys()
    {
        var keys = new List<string>
        {
            "version",
            "variables",
            "secrets"
        };

        if (_config.Docker != null) keys.Add("docker");
        if (_config.Artifacts != null) keys.Add("artifacts");
        if (_config.Logging != null) keys.Add("logging");
        if (_config.Features != null) keys.Add("features");

        return keys;
    }

    private static IEnumerable<string> GetKeysForObject(object obj)
    {
        if (obj is Dictionary<string, string> dict)
        {
            return dict.Keys;
        }

        var type = obj.GetType();
        var properties = GetCachedProperties(type);

        return properties
            .Where(p => p.GetValue(obj) != null)
            .Select(p => ToCamelCase(p.Name));
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
