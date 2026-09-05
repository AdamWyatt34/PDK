namespace PDK.Runners.StepExecutors;

/// <summary>
/// The shells a script step can run under.
/// </summary>
internal enum ScriptShell
{
    /// <summary>GNU bash (<c>bash --noprofile --norc -eo pipefail</c>, GitHub semantics).</summary>
    Bash,

    /// <summary>POSIX sh (<c>sh -e</c>).</summary>
    Sh,

    /// <summary>PowerShell 7 (<c>pwsh</c>).</summary>
    Pwsh,

    /// <summary>Windows PowerShell 5 (<c>powershell</c>).</summary>
    PowerShell,

    /// <summary>Python 3 (<c>python3</c>, falling back to <c>python</c>).</summary>
    Python,

    /// <summary>Windows command prompt (<c>cmd.exe</c>, Windows hosts only).</summary>
    Cmd
}

/// <summary>
/// Shell-specific knowledge shared by the Docker and host script executors: how to name the interpreter,
/// how to wrap the script and how to invoke it, matching the GitHub Actions shell templates.
/// </summary>
internal static class ScriptShellSupport
{
    /// <summary>
    /// Resolves the requested shell name. Accepts the plain names (bash, sh, pwsh, powershell, python, cmd),
    /// paths (<c>/bin/bash</c>), <c>.exe</c> suffixes and GitHub-style templates (<c>bash -e {0}</c>: the first
    /// token names the shell). An empty name selects bash (cmd on Windows hosts).
    /// </summary>
    public static bool TryResolve(string? shellName, OperatingSystemPlatform platform, out ScriptShell shell, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(shellName))
        {
            shell = platform == OperatingSystemPlatform.Windows ? ScriptShell.Cmd : ScriptShell.Bash;
            return true;
        }

        var token = shellName.Trim();
        var space = token.IndexOf(' ');
        if (space > 0)
        {
            token = token[..space];
        }

        token = token.Replace('\\', '/');
        var slash = token.LastIndexOf('/');
        if (slash >= 0)
        {
            token = token[(slash + 1)..];
        }

        if (token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^4];
        }

        switch (token.ToLowerInvariant())
        {
            case "bash":
                shell = ScriptShell.Bash;
                return true;
            case "sh":
                shell = ScriptShell.Sh;
                return true;
            case "pwsh":
                shell = ScriptShell.Pwsh;
                return true;
            case "powershell":
                shell = ScriptShell.PowerShell;
                return true;
            case "python":
            case "python3":
            case "py":
                shell = ScriptShell.Python;
                return true;
            case "cmd":
                shell = ScriptShell.Cmd;
                return true;
            default:
                shell = ScriptShell.Bash;
                error = $"Unsupported shell '{shellName}'. Supported shells: bash, sh, pwsh, powershell, python, cmd.";
                return false;
        }
    }

    /// <summary>Gets the display name of a shell.</summary>
    public static string GetDisplayName(ScriptShell shell) => shell switch
    {
        ScriptShell.Bash => "bash",
        ScriptShell.Sh => "sh",
        ScriptShell.Pwsh => "pwsh",
        ScriptShell.PowerShell => "powershell",
        ScriptShell.Python => "python",
        ScriptShell.Cmd => "cmd",
        _ => shell.ToString().ToLowerInvariant()
    };

    /// <summary>Gets the script file extension for a shell.</summary>
    public static string GetFileExtension(ScriptShell shell) => shell switch
    {
        ScriptShell.Pwsh or ScriptShell.PowerShell => ".ps1",
        ScriptShell.Python => ".py",
        ScriptShell.Cmd => ".cmd",
        _ => ".sh"
    };

    /// <summary>
    /// Gets the executables that can run the shell, in order of preference
    /// (<c>powershell</c> falls back to <c>pwsh</c>, <c>python3</c> to <c>python</c>).
    /// </summary>
    public static IReadOnlyList<string> GetExecutableCandidates(ScriptShell shell) => shell switch
    {
        ScriptShell.Bash => new[] { "bash" },
        ScriptShell.Sh => new[] { "sh" },
        ScriptShell.Pwsh => new[] { "pwsh" },
        ScriptShell.PowerShell => new[] { "powershell", "pwsh" },
        ScriptShell.Python => new[] { "python3", "python" },
        ScriptShell.Cmd => new[] { "cmd.exe" },
        _ => Array.Empty<string>()
    };

    /// <summary>
    /// Maps the executable that was found back to the shell whose invocation rules apply
    /// (e.g. a <c>powershell</c> request served by <c>pwsh</c>).
    /// </summary>
    public static ScriptShell ShellForExecutable(ScriptShell requested, string executable)
    {
        var name = Path.GetFileName(executable.Replace('\\', '/'));
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (requested == ScriptShell.PowerShell && string.Equals(name, "pwsh", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptShell.Pwsh;
        }

        return requested;
    }

    /// <summary>
    /// Prepares the script text: LF line endings (CRLF for cmd), a trailing newline, and for PowerShell
    /// the GitHub wrapper (<c>$ErrorActionPreference = 'stop'</c> prefix and a <c>$LASTEXITCODE</c> suffix) so
    /// non-terminating errors and native exit codes fail the step.
    /// </summary>
    public static string PrepareContent(ScriptShell shell, string script)
    {
        var normalized = script.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        switch (shell)
        {
            case ScriptShell.Pwsh:
            case ScriptShell.PowerShell:
                return "$ErrorActionPreference = 'stop'\n" +
                       normalized.TrimEnd('\n') + "\n" +
                       "if ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }\n";

            case ScriptShell.Cmd:
                if (!normalized.EndsWith('\n'))
                {
                    normalized += "\n";
                }

                return normalized.Replace("\n", "\r\n", StringComparison.Ordinal);

            default:
                return normalized.EndsWith('\n') ? normalized : normalized + "\n";
        }
    }

    /// <summary>
    /// Builds the interpreter arguments (without the executable) that run a script file, following the
    /// GitHub Actions templates: <c>bash --noprofile --norc -eo pipefail {0}</c>, <c>sh -e {0}</c>,
    /// <c>pwsh -Command ". '{0}'"</c>, <c>python {0}</c>, <c>cmd /d /s /c "{0}"</c>.
    /// </summary>
    public static IReadOnlyList<string> BuildInterpreterArguments(ScriptShell shell, string scriptPath) => shell switch
    {
        ScriptShell.Bash => new[] { "--noprofile", "--norc", "-eo", "pipefail", scriptPath },
        ScriptShell.Sh => new[] { "-e", scriptPath },
        ScriptShell.Pwsh => new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", $". '{EscapePowerShellLiteral(scriptPath)}'"
        },
        ScriptShell.PowerShell => new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Unrestricted", "-Command",
            $". '{EscapePowerShellLiteral(scriptPath)}'"
        },
        ScriptShell.Python => new[] { scriptPath },
        ScriptShell.Cmd => new[] { "/d", "/s", "/c", $"\"{scriptPath}\"" },
        _ => new[] { scriptPath }
    };

    /// <summary>
    /// Gets a hint for installing a missing shell.
    /// </summary>
    public static string GetInstallHint(ScriptShell shell, bool inContainer) => shell switch
    {
        ScriptShell.Pwsh or ScriptShell.PowerShell => inContainer
            ? "Install PowerShell in the image (Debian/Ubuntu: apt-get install -y powershell, Alpine: apk add --no-cache powershell) " +
              "or use an image with PowerShell pre-installed such as mcr.microsoft.com/powershell."
            : "Install PowerShell 7 from https://aka.ms/powershell.",
        ScriptShell.Python => inContainer
            ? "Install python3 in the image or use a python image (python:3.12)."
            : "Install Python 3 from https://www.python.org/downloads/.",
        ScriptShell.Bash => inContainer
            ? "Install bash in the image (apk add bash) or use shell: sh."
            : "Install bash (Git for Windows provides Git Bash) or use shell: pwsh / cmd.",
        ScriptShell.Sh => "The image has no POSIX shell; use an image with /bin/sh.",
        ScriptShell.Cmd => "cmd.exe is only available on Windows.",
        _ => string.Empty
    };

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
