namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;

/// <summary>
/// Builds <c>docker</c> command lines for docker steps (GitHub <c>docker/build-push-action</c> and Azure
/// <c>Docker@2</c> input conventions).
/// </summary>
internal static class DockerCommandSupport
{
    /// <summary>The supported <c>command</c> values.</summary>
    public static readonly string[] SupportedCommands =
    {
        "build", "buildAndPush", "push", "tag", "run", "login", "logout"
    };

    /// <summary>
    /// Builds the command lines for a step. <c>login</c>/<c>logout</c> produce no commands and a note.
    /// </summary>
    public static bool TryBuildCommands(
        Step step,
        Func<string, string> quote,
        out IReadOnlyList<string> commands,
        out string? note,
        out string? error)
    {
        commands = Array.Empty<string>();
        note = null;
        error = null;

        var command = StepExecutionHelpers.GetInput(step, "command");
        if (command == null)
        {
            error = $"The 'command' input is required for docker step '{step.Name}'. " +
                    $"Supported commands: {string.Join(", ", SupportedCommands)}";
            return false;
        }

        var normalized = SupportedCommands.FirstOrDefault(c => string.Equals(c, command, StringComparison.OrdinalIgnoreCase));
        if (normalized == null)
        {
            error = $"Unsupported docker command '{command}' in step '{step.Name}'. " +
                    $"Supported commands: {string.Join(", ", SupportedCommands)}";
            return false;
        }

        var tags = ResolveTags(step);
        var push = StepExecutionHelpers.GetBoolInput(step, false, "push");
        var list = new List<string>();

        switch (normalized)
        {
            case "login":
            case "logout":
                note = $"docker {normalized}: no-op in PDK - the local Docker credentials (docker login) are used as-is.";
                return true;

            case "build":
                list.Add(BuildBuildCommand(step, tags, quote));
                if (push)
                {
                    if (tags.Count == 0)
                    {
                        error = $"Pushing requires at least one tag ('tags' or 'repository' input) in docker step '{step.Name}'.";
                        return false;
                    }

                    list.AddRange(tags.Select(t => $"docker push {quote(t)}"));
                }

                break;

            case "buildAndPush":
                if (tags.Count == 0)
                {
                    error = $"The 'buildAndPush' command requires at least one tag ('tags' or 'repository' input) in docker step '{step.Name}'.";
                    return false;
                }

                list.Add(BuildBuildCommand(step, tags, quote));
                list.AddRange(tags.Select(t => $"docker push {quote(t)}"));
                break;

            case "push":
            {
                var image = StepExecutionHelpers.GetInput(step, "image");
                var targets = image != null ? new List<string> { image } : tags.ToList();
                if (targets.Count == 0)
                {
                    error = $"The 'image' input (or 'tags'/'repository') is required for docker push command in step '{step.Name}'.";
                    return false;
                }

                list.AddRange(targets.Select(t => $"docker push {quote(t)}"));
                break;
            }

            case "tag":
            {
                var source = StepExecutionHelpers.GetInput(step, "sourceImage", "source");
                if (source == null)
                {
                    error = $"The 'sourceImage' input is required for docker tag command in step '{step.Name}'.";
                    return false;
                }

                var target = StepExecutionHelpers.GetInput(step, "targetTag", "target");
                if (target == null)
                {
                    error = $"The 'targetTag' input is required for docker tag command in step '{step.Name}'.";
                    return false;
                }

                list.Add($"docker tag {quote(source)} {quote(target)}");
                break;
            }

            case "run":
            {
                var image = StepExecutionHelpers.GetInput(step, "image");
                if (image == null)
                {
                    error = $"The 'image' input is required for docker run command in step '{step.Name}'.";
                    return false;
                }

                var parts = new List<string> { "docker", "run" };
                var arguments = StepExecutionHelpers.GetInput(step, "arguments", "args");
                if (arguments != null)
                {
                    parts.Add(arguments);
                }

                parts.Add(quote(image));
                list.Add(string.Join(" ", parts));
                break;
            }
        }

        commands = list;
        return true;
    }

    /// <summary>
    /// Resolves the image tags: <c>tags</c> (newline/comma separated) optionally combined with
    /// <c>repository</c> (and <c>containerRegistry</c> when it is a host name), as Azure <c>Docker@2</c> does.
    /// </summary>
    internal static IReadOnlyList<string> ResolveTags(Step step)
    {
        var tags = StepExecutionHelpers.SplitList(StepExecutionHelpers.GetInput(step, "tags", "tag"));
        var repository = StepExecutionHelpers.GetInput(step, "repository");
        if (repository == null)
        {
            return tags;
        }

        var registry = StepExecutionHelpers.GetInput(step, "containerRegistry", "registry");
        var prefix = registry != null && (registry.Contains('.') || registry.Contains(':') ||
                                          string.Equals(registry, "localhost", StringComparison.OrdinalIgnoreCase))
            ? registry.TrimEnd('/') + "/"
            : string.Empty;

        if (tags.Count == 0)
        {
            tags = new[] { "latest" };
        }

        return tags
            .Select(t => t.Contains(':') || t.Contains('/') ? t : $"{prefix}{repository}:{t}")
            .ToList();
    }

    private static string BuildBuildCommand(Step step, IReadOnlyList<string> tags, Func<string, string> quote)
    {
        var parts = new List<string> { "docker", "build" };

        var dockerfile = StepExecutionHelpers.GetInput(step, "file", "Dockerfile", "dockerfile") ?? "Dockerfile";
        parts.Add("-f");
        parts.Add(quote(dockerfile));

        foreach (var tag in tags)
        {
            parts.Add("-t");
            parts.Add(quote(tag));
        }

        foreach (var buildArg in StepExecutionHelpers.SplitList(StepExecutionHelpers.GetInput(step, "buildArgs", "build-args", "buildargs")))
        {
            parts.Add("--build-arg");
            parts.Add(quote(buildArg));
        }

        var target = StepExecutionHelpers.GetInput(step, "target");
        if (target != null)
        {
            parts.Add("--target");
            parts.Add(quote(target));
        }

        var arguments = StepExecutionHelpers.GetInput(step, "arguments", "args");
        if (arguments != null)
        {
            parts.Add(arguments);
        }

        var context = StepExecutionHelpers.GetInput(step, "context", "buildContext", "path") ?? ".";
        parts.Add(quote(context));

        return string.Join(" ", parts);
    }
}
