namespace PDK.Core.Variables;

using System.Reflection;

/// <summary>
/// Provides built-in PDK variables including system information and PDK-specific values.
/// </summary>
public class BuiltInVariables : IBuiltInVariables
{
    private VariableContext _context;
    private static readonly string PdkVersion;

    /// <summary>
    /// Names of all built-in variables.
    /// </summary>
    private static readonly HashSet<string> BuiltInNames = new(StringComparer.Ordinal)
    {
        "PDK_VERSION",
        "PDK_WORKSPACE",
        "PDK_RUNNER",
        "PDK_JOB",
        "PDK_STEP",
        "HOME",
        "USER",
        "PWD",
        "TIMESTAMP",
        "TIMESTAMP_UNIX"
    };

    static BuiltInVariables()
    {
        // Get version from assembly, defaulting to 1.0.0 if not found
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        PdkVersion = version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "1.0.0";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltInVariables"/> class.
    /// </summary>
    public BuiltInVariables()
    {
        _context = VariableContext.CreateDefault();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltInVariables"/> class with a context.
    /// </summary>
    /// <param name="context">The initial variable context.</param>
    public BuiltInVariables(VariableContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc/>
    public string? GetValue(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return name switch
        {
            "PDK_VERSION" => PdkVersion,
            "PDK_WORKSPACE" => _context.Workspace ?? Environment.CurrentDirectory,
            "PDK_RUNNER" => _context.Runner ?? "local",
            "PDK_JOB" => _context.JobName,
            "PDK_STEP" => _context.StepName,
            "HOME" => GetHomeDirectory(),
            "USER" => GetUsername(),
            "PWD" => Environment.CurrentDirectory,
            "TIMESTAMP" => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            "TIMESTAMP_UNIX" => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            _ => null
        };
    }

    /// <inheritdoc/>
    public bool IsBuiltIn(string name)
    {
        return !string.IsNullOrEmpty(name) && BuiltInNames.Contains(name);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetAllNames()
    {
        return BuiltInNames;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetAll()
    {
        var result = new Dictionary<string, string>();

        foreach (var name in BuiltInNames)
        {
            var value = GetValue(name);
            if (value != null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public void UpdateContext(VariableContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    private static string GetHomeDirectory()
    {
        // Try UserProfile first (Windows), then HOME (Unix)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME");
        }
        return home ?? string.Empty;
    }

    private static string GetUsername()
    {
        // Try USERNAME (Windows), then USER (Unix)
        var user = Environment.GetEnvironmentVariable("USERNAME");
        if (string.IsNullOrEmpty(user))
        {
            user = Environment.GetEnvironmentVariable("USER");
        }
        if (string.IsNullOrEmpty(user))
        {
            user = Environment.UserName;
        }
        return user ?? string.Empty;
    }
}
