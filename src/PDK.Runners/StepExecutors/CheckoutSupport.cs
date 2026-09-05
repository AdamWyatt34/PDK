namespace PDK.Runners.StepExecutors;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// The state of a checkout target directory.
/// </summary>
internal enum WorkspaceState
{
    /// <summary>The directory does not exist or is empty.</summary>
    Empty,

    /// <summary>The directory contains files but no <c>.git</c>.</summary>
    Files,

    /// <summary>The directory contains a git repository (<c>.git</c> file or directory).</summary>
    Git
}

/// <summary>
/// The operations a checkout needs from its execution environment (container or host).
/// </summary>
internal interface ICheckoutShell
{
    /// <summary>Resolves the checkout target: the workspace, or <paramref name="relativePath"/> inside it.</summary>
    string ResolveDirectory(string? relativePath);

    /// <summary>Determines whether a directory is empty, contains files, or is a git repository.</summary>
    Task<WorkspaceState> ProbeAsync(string directory, CancellationToken cancellationToken);

    /// <summary>Creates a directory (and parents) when it does not exist.</summary>
    Task EnsureDirectoryAsync(string directory, CancellationToken cancellationToken);

    /// <summary>Runs <c>git</c> with the given arguments in a working directory.</summary>
    Task<ExecutionResult> RunGitAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Parsed inputs of a checkout step (GitHub <c>actions/checkout</c> and Azure <c>checkout:</c> conventions).
/// </summary>
internal sealed class CheckoutParameters
{
    private static readonly Regex GitHubShorthand = new(
        "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex CommitSha = new(
        "^[0-9a-fA-F]{40}$|^[0-9a-fA-F]{64}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private CheckoutParameters()
    {
    }

    /// <summary>Gets the repository URL or path to clone, or null for a self checkout.</summary>
    public string? Repository { get; private init; }

    /// <summary>Gets a value indicating whether the step checks out the current workspace.</summary>
    public bool IsSelf => Repository == null;

    /// <summary>Gets the ref (branch, tag, SHA or <c>refs/...</c>) to check out, or null for the default branch.</summary>
    public string? Ref { get; private init; }

    /// <summary>Gets the relative directory inside the workspace to check out into, or null for the workspace root.</summary>
    public string? Path { get; private init; }

    /// <summary>Gets the fetch depth, or null for full history.</summary>
    public int? FetchDepth { get; private init; }

    /// <summary>Gets a value indicating whether submodules are checked out.</summary>
    public bool Submodules { get; private init; }

    /// <summary>Gets the token used for HTTPS authentication, or null.</summary>
    public string? Token { get; private init; }

    /// <summary>Gets a value indicating whether the ref is an unexpanded expression and must be ignored.</summary>
    public bool RefIsExpression => Ref != null && StepExecutionHelpers.IsUnexpandedExpression(Ref);

    /// <summary>
    /// Gets a value indicating whether the ref must be fetched explicitly after cloning
    /// (commit SHAs and <c>refs/...</c> cannot be passed to <c>git clone --branch</c>).
    /// </summary>
    public bool RefNeedsFetch => Ref != null && !RefIsExpression &&
                                 (CommitSha.IsMatch(Ref) || Ref.StartsWith("refs/", StringComparison.Ordinal));

    /// <summary>Gets a value indicating whether a usable ref was requested.</summary>
    public bool HasRef => Ref != null && !RefIsExpression;

    /// <summary>Parses the checkout inputs of a step.</summary>
    public static CheckoutParameters FromStep(Step step)
    {
        var depthText = StepExecutionHelpers.GetInput(step, "fetch-depth", "fetchDepth");
        int? depth = null;
        if (depthText != null && int.TryParse(depthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDepth) && parsedDepth > 0)
        {
            depth = parsedDepth;
        }

        var submodulesText = StepExecutionHelpers.GetInput(step, "submodules");
        var submodules = submodulesText != null &&
                         (string.Equals(submodulesText, "recursive", StringComparison.OrdinalIgnoreCase) ||
                          StepExecutionHelpers.GetBoolInput(step, false, "submodules"));

        var token = StepExecutionHelpers.GetInput(step, "token", "github-token");
        if (token != null && StepExecutionHelpers.IsUnexpandedExpression(token))
        {
            token = null;
        }

        var path = StepExecutionHelpers.GetInput(step, "path");
        if (path != null && StepExecutionHelpers.IsUnexpandedExpression(path))
        {
            path = null;
        }

        return new CheckoutParameters
        {
            Repository = NormalizeRepository(StepExecutionHelpers.GetInput(step, "repository", "repo")),
            Ref = StepExecutionHelpers.GetInput(step, "ref", "branch", "tag"),
            Path = path,
            FetchDepth = depth,
            Submodules = submodules,
            Token = token
        };
    }

    /// <summary>
    /// Normalizes a repository input: <c>self</c>/empty means the current workspace; GitHub shorthand
    /// (<c>owner/repo</c>) becomes <c>https://github.com/owner/repo</c>; URLs, SSH and paths pass through.
    /// </summary>
    public static string? NormalizeRepository(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "self", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            if (GitHubShorthand.IsMatch(trimmed))
            {
                return $"https://github.com/{trimmed}";
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Fall through and use the value as given.
        }

        return trimmed;
    }

    /// <summary>Builds the <c>git clone</c> arguments (without <c>git</c>).</summary>
    public IReadOnlyList<string> BuildCloneArguments(string destination)
    {
        var arguments = new List<string> { "clone" };

        if (Token != null)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{Token}"));
            arguments.Add("-c");
            arguments.Add($"http.extraheader=AUTHORIZATION: basic {basic}");
        }

        if (FetchDepth is > 0)
        {
            arguments.Add("--depth");
            arguments.Add(FetchDepth.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Submodules)
        {
            arguments.Add("--recurse-submodules");
        }

        if (HasRef && !RefNeedsFetch)
        {
            arguments.Add("--branch");
            arguments.Add(Ref!);
        }

        arguments.Add("--");
        arguments.Add(Repository!);
        arguments.Add(destination);
        return arguments;
    }

    /// <summary>Builds the <c>git fetch</c> arguments used before checking out a SHA or <c>refs/...</c>.</summary>
    public IReadOnlyList<string> BuildFetchArguments()
    {
        var arguments = new List<string> { "fetch" };
        if (FetchDepth is > 0)
        {
            arguments.Add("--depth");
            arguments.Add(FetchDepth.Value.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("origin");
        arguments.Add(Ref!);
        return arguments;
    }

    /// <summary>Builds the <c>git checkout</c> arguments for the ref.</summary>
    public IReadOnlyList<string> BuildCheckoutArguments()
    {
        if (Ref!.StartsWith("refs/", StringComparison.Ordinal))
        {
            return new[] { "checkout", "--detach", "FETCH_HEAD" };
        }

        return new[] { "checkout", Ref };
    }

    /// <summary>Returns the repository with any embedded credentials removed, for messages and logs.</summary>
    public string DisplayRepository => Repository == null ? "(self)" : RedactCredentials(Repository);

    private static string RedactCredentials(string repository)
    {
        var schemeIndex = repository.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex < 0)
        {
            return repository;
        }

        var at = repository.IndexOf('@', schemeIndex + 3);
        var slash = repository.IndexOf('/', schemeIndex + 3);
        if (at > 0 && (slash < 0 || at < slash))
        {
            return repository[..(schemeIndex + 3)] + "***@" + repository[(at + 1)..];
        }

        return repository;
    }
}

/// <summary>
/// The checkout flow shared by the Docker and host executors.
/// </summary>
internal static class CheckoutFlow
{
    /// <summary>Environment applied to every git invocation.</summary>
    public static readonly IReadOnlyDictionary<string, string> GitEnvironment = new Dictionary<string, string>
    {
        ["GIT_TERMINAL_PROMPT"] = "0"
    };

    public static async Task<StepExecutionResult> RunAsync(
        Step step,
        ICheckoutShell shell,
        DateTimeOffset startTime,
        CancellationToken cancellationToken)
    {
        var parameters = CheckoutParameters.FromStep(step);
        var output = new StringBuilder();
        var target = shell.ResolveDirectory(parameters.Path);

        var state = await shell.ProbeAsync(target, cancellationToken).ConfigureAwait(false);

        if (parameters.IsSelf)
        {
            output.AppendLine("Using local workspace (self checkout)");

            if (state == WorkspaceState.Git)
            {
                output.AppendLine("Workspace contains git repository - using as-is");

                if (parameters.RefIsExpression)
                {
                    output.AppendLine($"Ignoring ref '{parameters.Ref}' (unexpanded expression)");
                }
                else if (parameters.HasRef)
                {
                    var checkout = await shell.RunGitAsync(parameters.BuildCheckoutArguments(), target, cancellationToken).ConfigureAwait(false);
                    AppendGitOutput(output, checkout);
                    if (!checkout.Success)
                    {
                        return StepExecutionHelpers.Failed(
                            step.Name,
                            $"Failed to checkout ref '{parameters.Ref}' in the workspace. Exit code: {checkout.ExitCode}{GitError(checkout)}",
                            startTime,
                            checkout.ExitCode,
                            output.ToString());
                    }

                    output.AppendLine($"Checked out {parameters.Ref}");
                }
            }
            else
            {
                output.AppendLine("Workspace ready (no git repository detected)");
                if (parameters.HasRef)
                {
                    output.AppendLine($"Ignoring ref '{parameters.Ref}': the workspace is not a git repository");
                }
            }

            return StepExecutionHelpers.Succeeded(step.Name, output.ToString(), startTime);
        }

        var repository = parameters.DisplayRepository;

        switch (state)
        {
            case WorkspaceState.Git:
            {
                output.AppendLine($"Repository already present in '{target}' - pulling latest changes");
                var pull = await shell.RunGitAsync(new[] { "pull", "--ff-only" }, target, cancellationToken).ConfigureAwait(false);
                AppendGitOutput(output, pull);
                if (!pull.Success)
                {
                    return StepExecutionHelpers.Failed(
                        step.Name,
                        $"Failed to pull latest changes in '{target}'. Exit code: {pull.ExitCode}{GitError(pull)}",
                        startTime,
                        pull.ExitCode,
                        output.ToString());
                }

                break;
            }

            case WorkspaceState.Files:
                output.AppendLine($"workspace already contains sources (no .git) - skipping clone of {repository}");
                return StepExecutionHelpers.Succeeded(step.Name, output.ToString(), startTime);

            default:
            {
                await shell.EnsureDirectoryAsync(target, cancellationToken).ConfigureAwait(false);

                var clone = await shell.RunGitAsync(parameters.BuildCloneArguments(target), target, cancellationToken).ConfigureAwait(false);
                AppendGitOutput(output, clone);
                if (!clone.Success)
                {
                    return StepExecutionHelpers.Failed(
                        step.Name,
                        $"Failed to clone repository {repository}. Exit code: {clone.ExitCode}{GitError(clone)}",
                        startTime,
                        clone.ExitCode,
                        output.ToString());
                }

                output.AppendLine($"Successfully cloned {repository}");
                break;
            }
        }

        if (parameters.RefIsExpression)
        {
            output.AppendLine($"Ignoring ref '{parameters.Ref}' (unexpanded expression)");
        }
        else if (parameters.HasRef && (state == WorkspaceState.Git || parameters.RefNeedsFetch))
        {
            if (parameters.RefNeedsFetch)
            {
                var fetch = await shell.RunGitAsync(parameters.BuildFetchArguments(), target, cancellationToken).ConfigureAwait(false);
                AppendGitOutput(output, fetch);
            }

            var checkout = await shell.RunGitAsync(parameters.BuildCheckoutArguments(), target, cancellationToken).ConfigureAwait(false);
            AppendGitOutput(output, checkout);
            if (!checkout.Success)
            {
                return StepExecutionHelpers.Failed(
                    step.Name,
                    $"Failed to checkout ref '{parameters.Ref}'. Exit code: {checkout.ExitCode}{GitError(checkout)}",
                    startTime,
                    checkout.ExitCode,
                    output.ToString());
            }

            output.AppendLine($"Checked out {parameters.Ref}");
        }

        return StepExecutionHelpers.Succeeded(step.Name, output.ToString(), startTime);
    }

    private static void AppendGitOutput(StringBuilder output, ExecutionResult result)
    {
        // git writes progress and status information to stderr even on success; keep everything in order.
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            output.AppendLine(result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            output.AppendLine(result.StandardError.TrimEnd());
        }
    }

    private static string GitError(ExecutionResult result)
    {
        return string.IsNullOrWhiteSpace(result.StandardError)
            ? string.Empty
            : $"{Environment.NewLine}Git error: {result.StandardError.Trim()}";
    }
}
