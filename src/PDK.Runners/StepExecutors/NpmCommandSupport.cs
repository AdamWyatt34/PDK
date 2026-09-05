namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;

/// <summary>
/// Builds <c>npm</c>/<c>npx</c> command lines shared by the Docker and host executors.
/// </summary>
internal static class NpmCommandSupport
{
    /// <summary>The supported <c>command</c> values.</summary>
    public static readonly string[] SupportedCommands =
    {
        "install", "ci", "build", "test", "run", "start", "publish", "custom", "npx"
    };

    /// <summary>
    /// Builds the command line for a step. Script arguments are passed after <c>--</c> for
    /// <c>run</c>/<c>test</c>/<c>start</c>/<c>build</c>; <c>custom</c> runs <c>npm &lt;customCommand&gt;</c> (Azure Npm@1);
    /// <c>npx</c> runs <c>npx &lt;arguments&gt;</c>.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="commandLine">The resulting command line.</param>
    /// <param name="tool">The executable that must be available (<c>npm</c> or <c>npx</c>).</param>
    /// <param name="error">The validation error when the inputs are incomplete.</param>
    public static bool TryBuildCommand(Step step, out string commandLine, out string tool, out string? error)
    {
        commandLine = string.Empty;
        tool = "npm";
        error = null;

        var command = StepExecutionHelpers.GetInput(step, "command") ?? "install";
        var normalized = SupportedCommands.FirstOrDefault(c => string.Equals(c, command, StringComparison.OrdinalIgnoreCase));
        if (normalized == null)
        {
            error = $"Unsupported npm command '{command}' in step '{step.Name}'. " +
                    $"Supported commands: {string.Join(", ", SupportedCommands)}";
            return false;
        }

        var script = StepExecutionHelpers.GetInput(step, "script");
        var arguments = StepExecutionHelpers.GetInput(step, "arguments", "args");
        var custom = StepExecutionHelpers.GetInput(step, "customCommand", "custom");

        switch (normalized)
        {
            case "run":
                if (script == null)
                {
                    error = $"The 'script' input is required when command is 'run' for npm step '{step.Name}'.";
                    return false;
                }

                commandLine = WithScriptArguments($"npm run {script}", arguments);
                return true;

            case "build":
                commandLine = WithScriptArguments("npm run build", arguments);
                return true;

            case "test":
                commandLine = WithScriptArguments("npm test", arguments);
                return true;

            case "start":
                commandLine = WithScriptArguments("npm start", arguments);
                return true;

            case "custom":
                if (custom == null)
                {
                    error = $"The 'customCommand' input is required when command is 'custom' for npm step '{step.Name}'.";
                    return false;
                }

                commandLine = arguments == null ? $"npm {custom}" : $"npm {custom} {arguments}";
                return true;

            case "npx":
            {
                var npxArguments = arguments ?? custom;
                if (npxArguments == null)
                {
                    error = $"The 'arguments' input is required when command is 'npx' for npm step '{step.Name}' (e.g. 'eslint .').";
                    return false;
                }

                tool = "npx";
                commandLine = $"npx {npxArguments}";
                return true;
            }

            default:
                commandLine = arguments == null ? $"npm {normalized}" : $"npm {normalized} {arguments}";
                return true;
        }
    }

    /// <summary>
    /// Gets the working directory input used by Azure <c>Npm@1</c> (<c>workingDir</c>) when the step has none.
    /// </summary>
    public static string? GetWorkingDirectory(Step step)
    {
        return step.WorkingDirectory ?? StepExecutionHelpers.GetInput(step, "workingDir", "working-directory");
    }

    private static string WithScriptArguments(string command, string? arguments)
    {
        return arguments == null ? command : $"{command} -- {arguments}";
    }
}
