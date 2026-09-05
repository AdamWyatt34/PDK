namespace PDK.Runners.StepExecutors;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using PDK.Core.Models;

/// <summary>
/// Parsed inputs of a dotnet step.
/// </summary>
internal sealed record DotnetInputs(
    string Command,
    string? Custom,
    string? Projects,
    string? Configuration,
    string? OutputPath,
    string? Arguments,
    bool NoBuild,
    bool NoRestore);

/// <summary>
/// Builds <c>dotnet</c> command lines shared by the Docker and host executors, and expands project globs.
/// </summary>
internal static class DotnetCommandSupport
{
    /// <summary>The supported <c>command</c> values.</summary>
    public static readonly string[] SupportedCommands =
    {
        "restore", "build", "test", "publish", "run", "pack", "clean", "custom", "tool"
    };

    private static readonly string[] ConfigurationCommands = { "build", "test", "publish", "run", "pack" };
    private static readonly string[] OutputCommands = { "build", "publish", "pack" };
    private static readonly string[] NoBuildCommands = { "test", "publish", "run", "pack" };
    private static readonly string[] NoRestoreCommands = { "build", "test", "publish", "run", "pack" };

    /// <summary>
    /// Parses and validates the step inputs.
    /// </summary>
    public static bool TryParse(Step step, out DotnetInputs inputs, out string? error)
    {
        inputs = null!;
        error = null;

        var command = StepExecutionHelpers.GetInput(step, "command");
        if (command == null)
        {
            error = $"The 'command' input is required for dotnet step '{step.Name}'. " +
                    $"Supported commands: {string.Join(", ", SupportedCommands)}";
            return false;
        }

        var normalized = SupportedCommands.FirstOrDefault(c => string.Equals(c, command, StringComparison.OrdinalIgnoreCase));
        if (normalized == null)
        {
            error = $"Unsupported dotnet command '{command}' in step '{step.Name}'. " +
                    $"Supported commands: {string.Join(", ", SupportedCommands)}";
            return false;
        }

        var custom = StepExecutionHelpers.GetInput(step, "custom");
        var arguments = StepExecutionHelpers.GetInput(step, "arguments", "args");

        if (normalized == "custom" && custom == null)
        {
            error = $"The 'custom' input (the dotnet subcommand to run) is required when command is 'custom' in step '{step.Name}'.";
            return false;
        }

        if (normalized == "tool" && arguments == null)
        {
            error = $"The 'arguments' input is required when command is 'tool' in step '{step.Name}' (e.g. 'install -g dotnet-format').";
            return false;
        }

        inputs = new DotnetInputs(
            normalized,
            custom,
            StepExecutionHelpers.GetInput(step, "projects", "project"),
            StepExecutionHelpers.GetInput(step, "configuration", "buildConfiguration"),
            StepExecutionHelpers.GetInput(step, "outputPath", "output", "outputDir"),
            arguments,
            StepExecutionHelpers.GetBoolInput(step, false, "nobuild", "noBuild", "no-build"),
            StepExecutionHelpers.GetBoolInput(step, false, "noRestore", "no-restore"));
        return true;
    }

    /// <summary>
    /// Splits a <c>projects</c> input into patterns (one per line, <c>;</c> also separates).
    /// </summary>
    public static IReadOnlyList<string> SplitProjectPatterns(string? projects)
    {
        if (string.IsNullOrWhiteSpace(projects))
        {
            return Array.Empty<string>();
        }

        return projects
            .Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    /// <summary>Checks if a pattern contains wildcard characters.</summary>
    public static bool ContainsWildcard(string pattern)
    {
        return pattern.Contains('*') || pattern.Contains('?') || pattern.StartsWith('!');
    }

    /// <summary>
    /// Expands project patterns on the host with <c>**</c> support (Microsoft.Extensions.FileSystemGlobbing).
    /// Literal paths pass through unchanged; wildcard patterns (and <c>!</c> exclusions) are matched below
    /// <paramref name="workingDirectory"/>. Returns null with an error when a wildcard matches nothing.
    /// </summary>
    public static IReadOnlyList<string>? ExpandProjectsOnHost(
        string? projects,
        string workingDirectory,
        string stepName,
        bool caseInsensitive,
        out string? error)
    {
        error = null;
        var patterns = SplitProjectPatterns(projects);
        if (patterns.Count == 0)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        var globPatterns = patterns.Where(ContainsWildcard).ToList();

        foreach (var pattern in patterns.Where(p => !ContainsWildcard(p)))
        {
            results.Add(pattern);
        }

        if (globPatterns.Count > 0)
        {
            var matcher = new Matcher(caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            foreach (var pattern in globPatterns)
            {
                if (pattern.StartsWith('!'))
                {
                    matcher.AddExclude(pattern[1..]);
                }
                else
                {
                    matcher.AddInclude(pattern);
                }
            }

            if (!Directory.Exists(workingDirectory))
            {
                error = $"Directory '{workingDirectory}' not found for pattern '{string.Join("; ", globPatterns)}' in step '{stepName}'.";
                return null;
            }

            var matches = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(workingDirectory)))
                .Files
                .Select(f => f.Path)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (matches.Count == 0)
            {
                error = $"No project files found matching pattern '{string.Join("; ", globPatterns)}' in step '{stepName}'. " +
                        "Please verify the project path or wildcard pattern.";
                return null;
            }

            results.AddRange(matches);
        }

        return results.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Builds the command lines to run: one per project (or a single one without a project).
    /// </summary>
    /// <param name="inputs">The parsed inputs.</param>
    /// <param name="projects">The expanded project paths.</param>
    /// <param name="quote">Quotes a path/argument for the target shell.</param>
    public static IReadOnlyList<string> BuildCommandLines(DotnetInputs inputs, IReadOnlyList<string> projects, Func<string, string> quote)
    {
        if (inputs.Command == "tool")
        {
            return new[] { $"dotnet tool {inputs.Arguments}".TrimEnd() };
        }

        var subcommand = inputs.Command == "custom" ? inputs.Custom! : inputs.Command;
        var targets = projects.Count > 0 ? projects.Cast<string?>().ToList() : new List<string?> { null };
        var lines = new List<string>();

        foreach (var target in targets)
        {
            var parts = new List<string> { "dotnet", subcommand };

            if (target != null)
            {
                parts.Add(quote(target));
            }

            if (!string.IsNullOrWhiteSpace(inputs.Configuration) && ConfigurationCommands.Contains(inputs.Command))
            {
                parts.Add("--configuration");
                parts.Add(quote(inputs.Configuration));
            }

            if (!string.IsNullOrWhiteSpace(inputs.OutputPath) && OutputCommands.Contains(inputs.Command))
            {
                parts.Add("--output");
                parts.Add(quote(inputs.OutputPath));
            }

            if (inputs.NoBuild && NoBuildCommands.Contains(inputs.Command))
            {
                parts.Add("--no-build");
            }

            if (inputs.NoRestore && NoRestoreCommands.Contains(inputs.Command))
            {
                parts.Add("--no-restore");
            }

            if (!string.IsNullOrWhiteSpace(inputs.Arguments))
            {
                parts.Add(inputs.Arguments);
            }

            lines.Add(string.Join(" ", parts));
        }

        return lines;
    }
}
