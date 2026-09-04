using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.Expressions;
using PDK.Core.Models;
using PDK.Core.Variables;

namespace PDK.Runners;

/// <summary>
/// What a runner should do with one step after conditions and expressions have been applied.
/// </summary>
public sealed record StepPlan
{
    /// <summary>The step with expressions expanded (name, script, inputs, env, working directory).</summary>
    public required Step Step { get; init; }

    /// <summary>True when the step must be skipped; <see cref="SkipReason"/> says why.</summary>
    public bool Skip { get; init; }

    /// <summary>Reason for skipping.</summary>
    public string? SkipReason { get; init; }

    /// <summary>True when the step is reported as a warning rather than silently skipped (unsupported action).</summary>
    public bool Warn { get; init; }

    /// <summary>True when the step failed before execution (invalid expression, unsupported step in strict mode).</summary>
    public bool Failed { get; init; }

    /// <summary>Failure message when <see cref="Failed"/> is true.</summary>
    public string? FailureMessage { get; init; }

    /// <summary>Environment variables for the step (platform, pipeline, job, dynamic and step-level values).</summary>
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Per-step timeout, or null.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// Per-job state shared by the Docker and host runners: expression contexts, the exported environment,
/// step outcomes (<c>steps.*</c>), values added via <c>$GITHUB_ENV</c>/<c>##vso[task.setvariable]</c>,
/// and the job status used by <c>success()</c>/<c>failure()</c>.
/// </summary>
public sealed class JobExecutionSession
{
    private static readonly Regex VsoCommand = new(@"^\s*##vso\[(?<cmd>[a-zA-Z.]+)(?<props>[^\]]*)\](?<value>.*)$", RegexOptions.Compiled | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
    private static readonly Regex WorkflowCommand = new(@"^::(?<cmd>set-output|set-env|add-path|add-mask)\s*(?<props>[^:]*)::(?<value>.*)$", RegexOptions.Compiled | RegexOptions.Multiline, TimeSpan.FromSeconds(1));

    private readonly Job _job;
    private readonly Pipeline _pipeline;
    private readonly JobRunContext _run;
    private readonly JobRuntimeInfo _info;
    private readonly ExpressionContext _jobContext;
    private readonly Dictionary<string, string> _baseEnvironment;
    private readonly List<StepOutcome> _completed = new();
    private readonly Dictionary<string, string> _dynamicEnv = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _dynamicVariables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _extraPaths = new();
    private readonly Dictionary<string, string> _outputs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _maskValues = new(StringComparer.Ordinal);
    private readonly ILogger? _logger;
    private readonly string _stepWorkspace;
    private readonly char _stepSeparator;

    /// <summary>Creates a session for one job.</summary>
    /// <param name="job">The job.</param>
    /// <param name="run">Run context.</param>
    /// <param name="stepWorkspace">Workspace path as seen by steps (container path in Docker mode).</param>
    /// <param name="containerImage">Container image, or null on the host.</param>
    /// <param name="logger">Optional logger.</param>
    public JobExecutionSession(Job job, JobRunContext run, string stepWorkspace, string? containerImage, ILogger? logger = null)
    {
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _logger = logger;
        _stepWorkspace = stepWorkspace;
        _stepSeparator = containerImage != null ? '/' : Path.DirectorySeparatorChar;
        _pipeline = run.Pipeline ?? new Pipeline { Name = job.Name, Provider = PipelineProvider.GitHub };

        var runtimeDir = HostRuntimeDirectory;
        Directory.CreateDirectory(runtimeDir);

        _info = new JobRuntimeInfo
        {
            Workspace = run.WorkspacePath,
            StepWorkspace = stepWorkspace,
            StepTempDirectory = StepPath(".pdk", "tmp"),
            Provider = _pipeline.Provider,
            PipelineName = _pipeline.Name,
            Secrets = run.Secrets,
            Variables = run.Variables,
            Inputs = run.Inputs,
            NeedsResults = run.NeedsResults,
            NeedsOutputs = run.NeedsOutputs,
            EventName = run.EventName,
            RunId = run.RunId,
            Git = GitInfo.Read(run.WorkspacePath),
            ContainerImage = containerImage,
            RunnerOs = containerImage != null ? "Linux" : DetectHostOs()
        };

        Directory.CreateDirectory(Path.Combine(run.WorkspacePath, ".pdk", "tmp"));
        _jobContext = PipelineContextBuilder.BuildJobContext(_pipeline, job, _info);
        _baseEnvironment = PipelineContextBuilder.BuildStepEnvironment(_pipeline, job, _info);

        foreach (var secret in run.Secrets.Values)
        {
            _maskValues.Add(secret);
        }

        if (IsGitHub)
        {
            WriteEventFile();
        }
    }

    /// <summary>Gets whether the pipeline uses GitHub syntax (otherwise Azure).</summary>
    public bool IsGitHub => _jobContext.Syntax == ExpressionSyntax.GitHub;

    /// <summary>Gets the current status used by status functions.</summary>
    public ExpressionJobStatus Status { get; private set; } = ExpressionJobStatus.Success;

    /// <summary>Gets the environment shared by every step (platform + pipeline + job values).</summary>
    public IReadOnlyDictionary<string, string> BaseEnvironment => _baseEnvironment;

    /// <summary>Gets outputs produced so far, keyed <c>stepId.name</c> and <c>name</c>.</summary>
    public IReadOnlyDictionary<string, string> Outputs => _outputs;

    /// <summary>Gets values that must be masked in output in addition to the registered secrets (<c>::add-mask::</c>, <c>isSecret=true</c>).</summary>
    public IReadOnlyCollection<string> AdditionalMaskValues => _maskValues;

    /// <summary>Host path of the per-job runtime directory (output/env/path files).</summary>
    public string HostRuntimeDirectory => Path.Combine(_run.WorkspacePath, ".pdk", "runtime", SanitizeSegment(_run.RunId), SanitizeSegment(_job.Id.Length > 0 ? _job.Id : _job.Name));

    /// <summary>
    /// Evaluates the step's condition and expands its expressions, producing the plan the runner executes.
    /// Never throws for pipeline-content problems: they surface as <see cref="StepPlan.Failed"/>.
    /// </summary>
    public StepPlan PrepareStep(Step step, int index)
    {
        ArgumentNullException.ThrowIfNull(step);

        var stepContext = PipelineContextBuilder.ForStep(_jobContext, step, CurrentDynamicEnv(), _completed, Status);
        if (!IsGitHub)
        {
            InjectDynamicVariables(stepContext);
        }

        if (!step.Enabled)
        {
            return new StepPlan { Step = step, Skip = true, SkipReason = "step is disabled (enabled: false)" };
        }

        // Condition
        bool shouldRun;
        try
        {
            shouldRun = ExpressionEvaluator.EvaluateCondition(step.Condition?.Expression, stepContext);
        }
        catch (ExpressionException ex)
        {
            return new StepPlan { Step = step, Failed = true, FailureMessage = $"Invalid condition: {ex.Message}" };
        }

        if (!shouldRun)
        {
            var reason = string.IsNullOrWhiteSpace(step.Condition?.Expression)
                ? (Status == ExpressionJobStatus.Cancelled ? "run was cancelled" : "a previous step failed")
                : $"condition '{step.Condition!.Expression.Trim()}' evaluated to false";
            return new StepPlan { Step = step, Skip = true, SkipReason = reason };
        }

        // Unsupported / setup steps
        if (step.Type == StepType.Setup)
        {
            return new StepPlan
            {
                Step = step,
                Skip = true,
                SkipReason = $"tool setup '{step.ActionReference ?? step.Name}' is provided by the runner environment"
            };
        }

        if (step.Type == StepType.Unknown)
        {
            var what = step.ActionReference ?? step.Name;
            if (_run.StrictUnsupportedSteps)
            {
                return new StepPlan { Step = step, Failed = true, FailureMessage = $"Unsupported step '{what}' (strict mode)" };
            }

            return new StepPlan { Step = step, Skip = true, Warn = true, SkipReason = $"unsupported action or task '{what}' was skipped" };
        }

        // Expand expressions in the step
        Step expanded;
        try
        {
            expanded = ExpandStep(step, stepContext);
        }
        catch (ExpressionException ex)
        {
            return new StepPlan { Step = step, Failed = true, FailureMessage = ex.Message };
        }

        // Environment
        var environment = new Dictionary<string, string>(_baseEnvironment, StringComparer.Ordinal);
        foreach (var (k, v) in _dynamicEnv)
        {
            environment[k] = v;
        }

        if (!IsGitHub)
        {
            foreach (var (k, v) in _dynamicVariables)
            {
                environment[PipelineContextBuilder.AzureEnvName(k)] = v;
            }
        }

        foreach (var (k, v) in expanded.Environment)
        {
            environment[k] = v;
        }

        environment["PDK_STEP"] = expanded.Name;

        if (_extraPaths.Count > 0)
        {
            var separator = _stepSeparator == '/' ? ':' : ';';
            var basePath = _info.ContainerImage != null
                ? "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
                : (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
            environment["PATH"] = string.Join(separator, _extraPaths.AsEnumerable().Reverse()) + separator + basePath;
        }

        if (IsGitHub)
        {
            var dir = HostStepRuntimeDirectory(index);
            Directory.CreateDirectory(dir);
            foreach (var file in new[] { "output", "env", "path", "summary" })
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, string.Empty);
                }
            }

            var stepDir = StepRuntimeDirectory(index);
            environment["GITHUB_OUTPUT"] = Join(stepDir, "output");
            environment["GITHUB_ENV"] = Join(stepDir, "env");
            environment["GITHUB_PATH"] = Join(stepDir, "path");
            environment["GITHUB_STEP_SUMMARY"] = Join(stepDir, "summary");
            environment["GITHUB_EVENT_PATH"] = StepPath(".pdk", "runtime", SanitizeSegment(_run.RunId), SanitizeSegment(_job.Id.Length > 0 ? _job.Id : _job.Name), "event.json");
            if (!string.IsNullOrEmpty(expanded.Id))
            {
                environment["GITHUB_ACTION"] = expanded.Id;
            }
        }

        return new StepPlan
        {
            Step = expanded,
            Environment = environment,
            Timeout = step.TimeoutMinutes is > 0 ? TimeSpan.FromMinutes(step.TimeoutMinutes.Value) : null
        };
    }

    /// <summary>
    /// Records the outcome of a step: updates the job status, the <c>steps.*</c> context, and harvests
    /// outputs / environment additions from the runtime files and workflow commands.
    /// </summary>
    public void Record(Step step, int index, StepExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(result);

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!result.Skipped)
        {
            if (IsGitHub)
            {
                HarvestGitHubFiles(index, outputs);
                HarvestWorkflowCommands(result.Output, outputs);
            }
            else
            {
                HarvestVsoCommands(result.Output, step, outputs);
            }
        }

        foreach (var (k, v) in outputs)
        {
            _outputs[k] = v;
            if (!string.IsNullOrEmpty(step.Id))
            {
                _outputs[$"{step.Id}.{k}"] = v;
            }
        }

        var outcome = result.Skipped ? "skipped" : result.Success ? "success" : (result.ExitCode == 130 ? "cancelled" : "failure");
        var conclusion = result.Skipped ? "skipped" : result.Success || result.AllowedFailure ? "success" : outcome;
        _completed.Add(new StepOutcome(step.Id, outcome, conclusion, outputs));

        if (!result.CountsAsSuccess && Status == ExpressionJobStatus.Success)
        {
            Status = result.ExitCode == 130 ? ExpressionJobStatus.Cancelled : ExpressionJobStatus.Failure;
        }
    }

    /// <summary>Marks the run as cancelled so remaining steps see <c>cancelled()</c>.</summary>
    public void MarkCancelled() => Status = ExpressionJobStatus.Cancelled;

    /// <summary>Removes the runtime directory created for this job.</summary>
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(HostRuntimeDirectory))
            {
                Directory.Delete(HostRuntimeDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Could not remove runtime directory {Dir}", HostRuntimeDirectory);
        }
    }

    /// <summary>Builds a skipped result for a step.</summary>
    public static StepExecutionResult SkippedResult(string stepName, string reason)
    {
        var now = DateTimeOffset.Now;
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = true,
            Skipped = true,
            SkipReason = reason,
            ExitCode = 0,
            Output = $"[SKIPPED] {reason}",
            Duration = TimeSpan.Zero,
            StartTime = now,
            EndTime = now
        };
    }

    /// <summary>Builds a failed result for a step that could not be executed.</summary>
    public static StepExecutionResult FailedResult(string stepName, string message, bool allowedFailure, int exitCode = -1)
    {
        var now = DateTimeOffset.Now;
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = false,
            AllowedFailure = allowedFailure,
            ExitCode = exitCode,
            Output = string.Empty,
            ErrorOutput = message,
            Duration = TimeSpan.Zero,
            StartTime = now,
            EndTime = now
        };
    }

    private Step ExpandStep(Step step, ExpressionContext context)
    {
        string Expand(string? text) => text == null ? string.Empty : TemplateExpander.Expand(text, context);

        var with = new Dictionary<string, string>(step.With.Count);
        foreach (var (k, v) in step.With)
        {
            with[k] = Expand(v);
        }

        var env = new Dictionary<string, string>(step.Environment.Count);
        foreach (var (k, v) in step.Environment)
        {
            env[k] = Expand(v);
        }

        return new Step
        {
            Id = step.Id,
            Name = Expand(step.Name),
            Type = step.Type,
            Script = step.Script == null ? null : Expand(step.Script),
            Shell = step.Shell,
            With = with,
            Environment = env,
            ContinueOnError = step.ContinueOnError,
            Condition = step.Condition,
            WorkingDirectory = step.WorkingDirectory == null ? null : Expand(step.WorkingDirectory),
            Artifact = step.Artifact == null ? null : step.Artifact with
            {
                Name = Expand(step.Artifact.Name),
                Patterns = step.Artifact.Patterns.Select(Expand).ToArray(),
                TargetPath = step.Artifact.TargetPath == null ? null : Expand(step.Artifact.TargetPath)
            },
            Needs = step.Needs,
            Enabled = step.Enabled,
            TimeoutMinutes = step.TimeoutMinutes,
            ActionReference = step.ActionReference
        };
    }

    private Dictionary<string, string> CurrentDynamicEnv()
    {
        var env = new Dictionary<string, string>(_dynamicEnv, StringComparer.Ordinal);
        return env;
    }

    private void InjectDynamicVariables(ExpressionContext context)
    {
        if (context.GetRoot("variables") is Dictionary<string, object?> variables)
        {
            foreach (var (k, v) in _dynamicVariables)
            {
                variables[k] = v;
            }
        }
    }

    private void HarvestGitHubFiles(int index, Dictionary<string, string> outputs)
    {
        var dir = HostStepRuntimeDirectory(index);
        foreach (var (name, value) in ParseKeyValueFile(Path.Combine(dir, "output")))
        {
            outputs[name] = value;
        }

        foreach (var (name, value) in ParseKeyValueFile(Path.Combine(dir, "env")))
        {
            _dynamicEnv[name] = value;
        }

        var pathFile = Path.Combine(dir, "path");
        if (File.Exists(pathFile))
        {
            foreach (var line in File.ReadAllLines(pathFile))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _extraPaths.Add(line.Trim());
                }
            }
        }
    }

    private void HarvestWorkflowCommands(string output, Dictionary<string, string> outputs)
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        foreach (Match m in WorkflowCommand.Matches(output))
        {
            var cmd = m.Groups["cmd"].Value;
            var props = ParseProps(m.Groups["props"].Value);
            var value = m.Groups["value"].Value.TrimEnd('\r');
            switch (cmd)
            {
                case "set-output" when props.TryGetValue("name", out var n):
                    outputs[n] = value;
                    break;
                case "set-env" when props.TryGetValue("name", out var n):
                    _dynamicEnv[n] = value;
                    break;
                case "add-path":
                    _extraPaths.Add(value);
                    break;
                case "add-mask":
                    _maskValues.Add(value);
                    break;
            }
        }
    }

    private void HarvestVsoCommands(string output, Step step, Dictionary<string, string> outputs)
    {
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        foreach (Match m in VsoCommand.Matches(output))
        {
            var cmd = m.Groups["cmd"].Value;
            var props = ParseProps(m.Groups["props"].Value);
            var value = m.Groups["value"].Value.TrimEnd('\r');
            switch (cmd.ToLowerInvariant())
            {
                case "task.setvariable" when props.TryGetValue("variable", out var name):
                    _dynamicVariables[name] = value;
                    if (props.TryGetValue("isoutput", out var isOutput) && string.Equals(isOutput, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        outputs[name] = value;
                    }

                    if (props.TryGetValue("issecret", out var isSecret) && string.Equals(isSecret, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        _maskValues.Add(value);
                    }

                    break;
                case "task.prependpath":
                    _extraPaths.Add(value);
                    break;
                case "task.setsecret":
                    _maskValues.Add(value);
                    break;
            }
        }
    }

    private static Dictionary<string, string> ParseProps(string text)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                props[part[..eq].Trim()] = part[(eq + 1)..].Trim();
            }
            else if (part.Trim().Length > 0)
            {
                props[part.Trim()] = "true";
            }
        }

        return props;
    }

    /// <summary>
    /// Parses a <c>$GITHUB_OUTPUT</c>/<c>$GITHUB_ENV</c> style file: <c>name=value</c> lines and
    /// heredoc blocks <c>name&lt;&lt;DELIM ... DELIM</c>.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> ParseKeyValueFile(string path)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var heredoc = line.IndexOf("<<", StringComparison.Ordinal);
            var eq = line.IndexOf('=');
            if (heredoc > 0 && (eq < 0 || heredoc < eq))
            {
                var name = line[..heredoc].Trim();
                var delimiter = line[(heredoc + 2)..].Trim();
                var sb = new StringBuilder();
                i++;
                while (i < lines.Length && lines[i] != delimiter)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append('\n');
                    }

                    sb.Append(lines[i]);
                    i++;
                }

                yield return new KeyValuePair<string, string>(name, sb.ToString());
                continue;
            }

            if (eq > 0)
            {
                yield return new KeyValuePair<string, string>(line[..eq].Trim(), line[(eq + 1)..]);
            }
        }
    }

    private void WriteEventFile()
    {
        try
        {
            var path = Path.Combine(HostRuntimeDirectory, "event.json");
            var evt = ExpressionEvaluator.Evaluate("github.event", _jobContext);
            File.WriteAllText(path, ExpressionValue.ToJson(evt));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExpressionException)
        {
            _logger?.LogDebug(ex, "Could not write event.json");
        }
    }

    private string HostStepRuntimeDirectory(int index) => Path.Combine(HostRuntimeDirectory, $"step-{index.ToString(CultureInfo.InvariantCulture)}");

    private string StepRuntimeDirectory(int index) => StepPath(".pdk", "runtime", SanitizeSegment(_run.RunId), SanitizeSegment(_job.Id.Length > 0 ? _job.Id : _job.Name), $"step-{index.ToString(CultureInfo.InvariantCulture)}");

    private string StepPath(params string[] parts) => Join(_stepWorkspace, parts);

    private string Join(string root, params string[] parts)
    {
        var sb = new StringBuilder(root.TrimEnd('/', '\\'));
        foreach (var part in parts)
        {
            sb.Append(_stepSeparator).Append(part);
        }

        return sb.ToString();
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "job";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
        }

        return sb.ToString();
    }

    private static string DetectHostOs()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        return "Linux";
    }
}
