using System.Globalization;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.ErrorHandling;
using PDK.Core.Expressions;
using PDK.Core.Models;

namespace PDK.Providers.GitLab;

/// <summary>
/// What the parser knows about the file and the run while converting a document.
/// </summary>
internal sealed class GitLabParseContext
{
    public required string DisplayPath { get; init; }

    public required string Workspace { get; init; }

    public required PipelineParseOptions Options { get; init; }

    public required GitInfo Git { get; init; }

    public required List<string> Warnings { get; init; }

    public ILogger? Logger { get; init; }

    public void Warn(string message)
    {
        if (!Warnings.Contains(message, StringComparer.Ordinal))
        {
            Warnings.Add(message);
        }
    }

    public void Debug(string message) => Logger?.LogDebug("{GitLabNote}", message);
}

/// <summary>
/// Converts a merged, reference-resolved GitLab CI document into the common <see cref="Pipeline"/> model.
/// </summary>
internal sealed class GitLabPipelineBuilder
{
    private const string DefaultRunner = "ubuntu-latest";
    private const string DefaultStage = "test";
    private const string PreStage = ".pre";
    private const string PostStage = ".post";

    private static readonly string[] DefaultStages = { PreStage, "build", "test", "deploy", PostStage };
    private static readonly System.Buffers.SearchValues<char> GlobCharacters = System.Buffers.SearchValues.Create("*?[");

    private static readonly HashSet<string> ReservedTopLevelKeys = new(StringComparer.Ordinal)
    {
        "stages", "types", "variables", "default", "include", "workflow", "image", "services", "cache",
        "before_script", "after_script", "spec"
    };

    private static readonly string[] DeprecatedGlobalDefaults = { "image", "services", "before_script", "after_script", "cache" };

    private static readonly HashSet<string> DefaultKeys = new(StringComparer.Ordinal)
    {
        "image", "services", "before_script", "after_script", "tags", "timeout", "retry", "cache", "artifacts",
        "interruptible", "hooks", "id_tokens", "identity"
    };

    private static readonly HashSet<string> KnownJobKeys = new(StringComparer.Ordinal)
    {
        "script", "before_script", "after_script", "image", "services", "stage", "variables", "needs", "dependencies",
        "rules", "only", "except", "when", "allow_failure", "timeout", "retry", "tags", "artifacts", "extends",
        "parallel", "interruptible", "resource_group", "environment", "coverage", "release", "secrets", "inherit",
        "cache", "trigger", "start_in", "id_tokens", "identity", "hooks", "pages", "publish", "dast_configuration",
        "manual_confirmation", "run"
    };

    private static readonly HashSet<string> KnownRuleKeys = new(StringComparer.Ordinal)
    {
        "if", "exists", "changes", "when", "allow_failure", "variables", "start_in", "needs", "interruptible", "auto_cancel"
    };

    private readonly GitLabMap _root;
    private readonly GitLabParseContext _ctx;
    private readonly Dictionary<string, GitLabMap> _templates = new(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, GitLabMap>> _jobMaps = new();
    private readonly Dictionary<string, GitLabMap> _jobMapsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GitLabMap> _resolvedExtends = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _cliVariables = new(StringComparer.Ordinal);

    private List<string> _stages = new();
    private GitLabMap _defaults = new();
    private Dictionary<string, string> _globalVariables = new(StringComparer.Ordinal);
    private HashSet<string> _globalNoExpand = new(StringComparer.Ordinal);
    private Dictionary<string, string> _predefinedPipeline = new(StringComparer.Ordinal);
    private string _pipelineName = string.Empty;
    private string? _workflowBlockedReason;

    public GitLabPipelineBuilder(GitLabMap root, GitLabParseContext context)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _ctx = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Converts the document.</summary>
    public Pipeline Build()
    {
        _pipelineName = Path.GetFileName(_ctx.DisplayPath);
        _predefinedPipeline = BuildPredefined(null, 1);

        _stages = ReadStages();
        _defaults = ReadDefaults();
        ReadGlobalVariables();
        ApplyWorkflow();
        _predefinedPipeline = BuildPredefined(null, 1);
        CollectJobs();

        var entries = new List<JobEntry>();
        foreach (var (name, map) in _jobMaps)
        {
            var extended = ResolveExtends(name, map, new List<string>());
            NoteIgnoredJobKeys(name, extended);
            var resolved = ApplyDefaults(name, extended);
            foreach (var instance in ExpandParallel(name, resolved))
            {
                entries.Add(BuildJob(name, resolved, instance));
            }
        }

        WireDependencies(entries);
        DetectCycles(entries);

        var pipeline = new Pipeline
        {
            Name = _pipelineName,
            Provider = PipelineProvider.GitLab,
            Variables = new Dictionary<string, string>(_globalVariables, StringComparer.Ordinal),
            DefaultBranch = _ctx.Git.DefaultBranch.Length > 0 ? _ctx.Git.DefaultBranch : null
        };

        foreach (var entry in entries)
        {
            pipeline.Jobs[entry.Job.Id] = entry.Job;
        }

        return pipeline;
    }

    // ---------------------------------------------------------------- top level

    private List<string> ReadStages()
    {
        var value = _root.ContainsKey("stages") ? _root["stages"] : _root["types"];
        if (value is null)
        {
            return DefaultStages.ToList();
        }

        if (value is not GitLabList list)
        {
            throw Structure("'stages' must be a list of stage names", position: value as GitLabMap);
        }

        var names = new List<string>();
        foreach (var item in list)
        {
            if (item is not string name || string.IsNullOrWhiteSpace(name))
            {
                throw Structure($"'stages' entries must be stage names, found {GitLabYaml.Describe(item)}", position: null, line: list.Line, column: list.Column);
            }

            var trimmed = name.Trim();
            if (trimmed is PreStage or PostStage || names.Contains(trimmed, StringComparer.Ordinal))
            {
                continue;
            }

            names.Add(trimmed);
        }

        names.Insert(0, PreStage);
        names.Add(PostStage);
        return names;
    }

    private GitLabMap ReadDefaults()
    {
        var defaults = new GitLabMap();
        if (_root.TryGetValue("default", out var value) && value is not null)
        {
            if (value is not GitLabMap map)
            {
                throw Structure("'default' must be a mapping of job keywords", value as GitLabMap);
            }

            foreach (var (key, entry) in map)
            {
                if (!DefaultKeys.Contains(key))
                {
                    _ctx.Warn($"'default:{key}' is not a keyword GitLab accepts under 'default' and is ignored.");
                    continue;
                }

                defaults.Set(key, entry);
            }
        }

        foreach (var key in DeprecatedGlobalDefaults)
        {
            if (_root.TryGetValue(key, out var global) && global is not null && !defaults.ContainsKey(key))
            {
                _ctx.Debug($"Top-level '{key}' is treated as 'default:{key}'.");
                defaults.Set(key, global);
            }
        }

        if (defaults.ContainsKey("services"))
        {
            _ctx.Warn("'default:services' (service containers) are not supported locally and will be ignored.");
        }

        return defaults;
    }

    private void ReadGlobalVariables()
    {
        var declared = ReadVariables(_root["variables"], "variables", out var noExpand);
        _globalNoExpand = noExpand;

        // Values set when "triggering" the pipeline (--param) override declared values and may add new ones
        foreach (var (name, value) in _ctx.Options.Parameters)
        {
            var index = declared.FindIndex(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                declared[index] = new KeyValuePair<string, string>(declared[index].Key, value);
            }
            else
            {
                declared.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        foreach (var (name, value) in _ctx.Options.Variables)
        {
            _cliVariables[name] = value;
            var index = declared.FindIndex(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                declared[index] = new KeyValuePair<string, string>(declared[index].Key, value);
            }
        }

        // Pipeline-level expansion: references that only resolve per job (CI_JOB_*, job variables) stay as
        // written and are expanded again for each job; values without references are final ($$ becomes $).
        _globalVariables = GitLabVariableExpander.ExpandAll(declared, PipelineOuterScope, _globalNoExpand, keepUndefined: true);
        foreach (var name in _globalVariables.Keys.ToList())
        {
            var value = _globalVariables[name];
            if (!_globalNoExpand.Contains(name) && !GitLabVariableExpander.ContainsReference(value))
            {
                _globalVariables[name] = GitLabVariableExpander.Expand(value, _ => null);
            }
        }
    }

    private string? PipelineOuterScope(string name)
    {
        if (_cliVariables.TryGetValue(name, out var cli))
        {
            return cli;
        }

        return _predefinedPipeline.TryGetValue(name, out var predefined) ? predefined : null;
    }

    private string? PipelineScope(string name)
    {
        if (_cliVariables.TryGetValue(name, out var cli))
        {
            return cli;
        }

        if (_globalVariables.TryGetValue(name, out var global))
        {
            return global;
        }

        return _predefinedPipeline.TryGetValue(name, out var predefined) ? predefined : null;
    }

    private void ApplyWorkflow()
    {
        if (!_root.TryGetValue("workflow", out var value) || value is null)
        {
            return;
        }

        if (value is not GitLabMap workflow)
        {
            throw Structure("'workflow' must be a mapping", value as GitLabMap);
        }

        if (workflow["name"] is string name && !string.IsNullOrWhiteSpace(name))
        {
            _pipelineName = GitLabVariableExpander.Expand(name, PipelineScope).Trim();
        }

        if (!workflow.ContainsKey("rules"))
        {
            return;
        }

        var outcome = EvaluateRules(workflow["rules"], PipelineScope, "workflow");
        if (!outcome.Matched)
        {
            _workflowBlockedReason = "workflow rules: no rule matched, the pipeline would not be created";
            return;
        }

        if (outcome.When == "never")
        {
            _workflowBlockedReason = $"workflow rules: {outcome.RuleText} -> when: never";
            return;
        }

        if (outcome.Variables.Count > 0)
        {
            foreach (var (k, v) in GitLabVariableExpander.ExpandAll(outcome.Variables, PipelineOuterScope, keepUndefined: true))
            {
                _globalVariables[k] = v;
            }
        }
    }

    private void CollectJobs()
    {
        foreach (var (key, value) in _root)
        {
            if (ReservedTopLevelKeys.Contains(key))
            {
                continue;
            }

            if (value is not GitLabMap map)
            {
                if (key.StartsWith('.'))
                {
                    continue;
                }

                if (value is null)
                {
                    throw Structure($"Job '{key}' is empty. A job must define at least 'script' (or 'trigger').", null, jobName: key);
                }

                _ctx.Warn($"Top-level keyword '{key}' is not a GitLab CI keyword and not a job (its value is {GitLabYaml.Describe(value)}); it is ignored.");
                continue;
            }

            if (key.StartsWith('.'))
            {
                _templates[key] = map;
                continue;
            }

            _jobMaps.Add(new KeyValuePair<string, GitLabMap>(key, map));
            _jobMapsByName[key] = map;
        }

        if (_jobMaps.Count == 0)
        {
            throw Structure(
                "The configuration does not define any job. A job is a top-level key with a 'script' (hidden jobs start with '.').",
                null,
                suggestions: new[] { "Add a job, e.g. build:\n  script:\n    - echo hello" });
        }
    }

    // ---------------------------------------------------------------- extends / default

    private GitLabMap ResolveExtends(string name, GitLabMap map, List<string> stack)
    {
        if (_resolvedExtends.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var parents = GitLabYaml.StringList(map["extends"]);
        if (parents.Count == 0)
        {
            return map;
        }

        if (stack.Contains(name, StringComparer.Ordinal))
        {
            throw PipelineParseException.CircularDependency(_ctx.DisplayPath, stack.Append(name));
        }

        stack.Add(name);
        object? merged = new GitLabMap { Line = map.Line, Column = map.Column };
        foreach (var parentName in parents)
        {
            if (!_templates.TryGetValue(parentName, out var parent) && !_jobMapsByName.TryGetValue(parentName, out parent))
            {
                throw Structure(
                    $"Job '{name}' extends '{parentName}', which is not defined.",
                    map,
                    jobName: name,
                    suggestions: new[] { $"Define a hidden job '{parentName}' (or a regular job with that name) or fix the 'extends' entry" });
            }

            merged = GitLabYaml.DeepMerge(merged, ResolveExtends(parentName, parent, stack));
        }

        var own = map.Clone();
        own.Remove("extends");
        var result = (GitLabMap)GitLabYaml.DeepMerge(merged, own)!;
        result.Remove("extends");
        stack.RemoveAt(stack.Count - 1);

        _resolvedExtends[name] = result;
        return result;
    }

    private GitLabMap ApplyDefaults(string name, GitLabMap map)
    {
        var result = map.Clone();
        var inheritDefaults = true;
        HashSet<string>? inheritOnly = null;

        if (result.TryGetValue("inherit", out var inheritValue) && inheritValue is not null)
        {
            if (inheritValue is GitLabMap inherit)
            {
                switch (inherit["default"])
                {
                    case string flag when GitLabYaml.Bool(flag) == false:
                        inheritDefaults = false;
                        break;
                    case GitLabList keys:
                        inheritOnly = new HashSet<string>(GitLabYaml.StringList(keys), StringComparer.Ordinal);
                        break;
                }

                if (inherit.ContainsKey("variables") && (inherit["variables"] is GitLabList || GitLabYaml.Bool(inherit["variables"] as string) == false))
                {
                    _ctx.Warn($"Job '{name}': 'inherit:variables' is not supported locally; every pipeline variable is exported to the job.");
                }
            }
            else
            {
                _ctx.Warn($"Job '{name}': 'inherit' must be a mapping; it is ignored.");
            }

            result.Remove("inherit");
        }

        if (inheritDefaults)
        {
            foreach (var (key, value) in _defaults)
            {
                if (inheritOnly is not null && !inheritOnly.Contains(key))
                {
                    continue;
                }

                if (!result.ContainsKey(key))
                {
                    result.Set(key, GitLabYaml.Clone(value));
                }
            }
        }

        return result;
    }

    // ---------------------------------------------------------------- parallel

    private List<JobInstance> ExpandParallel(string name, GitLabMap map)
    {
        var parallel = map["parallel"];
        if (parallel is null)
        {
            return new List<JobInstance> { new(name, new Dictionary<string, string>(StringComparer.Ordinal), null) };
        }

        if (parallel is string countText)
        {
            if (!int.TryParse(countText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 1)
            {
                throw Structure($"Job '{name}': 'parallel' must be a number (1-200) or a 'matrix' mapping.", map, jobName: name);
            }

            var total = count.ToString(CultureInfo.InvariantCulture);
            return Enumerable.Range(1, count)
                .Select(i => new JobInstance(
                    $"{name} {i.ToString(CultureInfo.InvariantCulture)}/{total}",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["CI_NODE_INDEX"] = i.ToString(CultureInfo.InvariantCulture),
                        ["CI_NODE_TOTAL"] = total
                    },
                    null))
                .ToList();
        }

        if (parallel is GitLabMap parallelMap && parallelMap["matrix"] is GitLabList matrix)
        {
            var combinations = new List<(string Label, Dictionary<string, string> Values)>();
            foreach (var entry in matrix)
            {
                if (entry is not GitLabMap entryMap || entryMap.Count == 0)
                {
                    throw Structure($"Job '{name}': each 'parallel:matrix' entry must be a mapping of variable names to values.", map, jobName: name);
                }

                var partial = new List<Dictionary<string, string>> { new(StringComparer.Ordinal) };
                foreach (var (variable, values) in entryMap)
                {
                    var options = GitLabYaml.StringList(values);
                    if (options.Count == 0)
                    {
                        throw Structure($"Job '{name}': 'parallel:matrix' variable '{variable}' has no values.", map, jobName: name);
                    }

                    partial = partial
                        .SelectMany(existing => options.Select(option =>
                        {
                            var next = new Dictionary<string, string>(existing, StringComparer.Ordinal) { [variable] = option };
                            return next;
                        }))
                        .ToList();
                }

                // GitLab names matrix jobs "job: [value1, value2]" in the order the variables are declared
                foreach (var combination in partial)
                {
                    combinations.Add((string.Join(", ", entryMap.Keys.Select(key => combination[key])), combination));
                }
            }

            if (combinations.Count == 0)
            {
                throw Structure($"Job '{name}': 'parallel:matrix' produced no jobs.", map, jobName: name);
            }

            var total = combinations.Count.ToString(CultureInfo.InvariantCulture);
            return combinations.Select((combination, index) =>
            {
                var variables = new Dictionary<string, string>(combination.Values, StringComparer.Ordinal)
                {
                    ["CI_NODE_INDEX"] = (index + 1).ToString(CultureInfo.InvariantCulture),
                    ["CI_NODE_TOTAL"] = total
                };
                return new JobInstance($"{name}: [{combination.Label}]", variables, combination.Values);
            }).ToList();
        }

        throw Structure($"Job '{name}': 'parallel' must be a number or a mapping with 'matrix'.", map, jobName: name);
    }

    // ---------------------------------------------------------------- jobs

    private JobEntry BuildJob(string baseName, GitLabMap map, JobInstance instance)
    {
        var name = instance.Name;
        foreach (var key in map.Keys)
        {
            if (!KnownJobKeys.Contains(key))
            {
                _ctx.Warn($"Job '{baseName}': '{key}' is not a GitLab CI job keyword and is ignored.");
            }
        }

        var stage = ReadStage(baseName, map);
        var isTrigger = map.ContainsKey("trigger") && map["trigger"] is not null;
        var scriptLines = GitLabYaml.ScriptLines(map["script"]);
        var hasRun = map.ContainsKey("run") && map["run"] is not null;

        if (!isTrigger && scriptLines.Count == 0 && !hasRun)
        {
            throw PipelineParseException.MissingRequiredField(_ctx.DisplayPath, "script", baseName);
        }

        // Variables: job-level + matrix/node values, then the globals that need job scope, expanded per job
        var predefinedJob = BuildPredefined(new Job { Name = name, Stage = stage }, 1);
        string? Outer(string key)
        {
            if (_cliVariables.TryGetValue(key, out var cli))
            {
                return cli;
            }

            if (_globalVariables.TryGetValue(key, out var global))
            {
                return GitLabVariableExpander.ContainsReference(global) && !_globalNoExpand.Contains(key)
                    ? GitLabVariableExpander.Expand(global, k => predefinedJob.TryGetValue(k, out var p) ? p : _cliVariables.TryGetValue(k, out var c) ? c : null)
                    : global;
            }

            return predefinedJob.TryGetValue(key, out var predefined) ? predefined : null;
        }

        var rawJobVariables = ReadVariables(map["variables"], $"jobs:{baseName}:variables", out var jobNoExpand);
        foreach (var (k, v) in instance.Variables)
        {
            rawJobVariables.RemoveAll(entry => entry.Key == k);
            rawJobVariables.Add(new KeyValuePair<string, string>(k, v));
        }

        foreach (var (k, v) in _globalVariables)
        {
            if (GitLabVariableExpander.ContainsReference(v) && !_globalNoExpand.Contains(k) && rawJobVariables.All(entry => entry.Key != k))
            {
                rawJobVariables.Add(new KeyValuePair<string, string>(k, v));
            }
        }

        var jobVariables = GitLabVariableExpander.ExpandAll(rawJobVariables, Outer, jobNoExpand);
        string? Scope(string key) => jobVariables.TryGetValue(key, out var v) ? v : Outer(key);

        var decision = Decide(baseName, name, map, Scope);
        if (decision.RuleVariables.Count > 0)
        {
            foreach (var (k, v) in GitLabVariableExpander.ExpandAll(decision.RuleVariables, Scope))
            {
                jobVariables[k] = v;
            }
        }

        var skipReason = _workflowBlockedReason ?? decision.SkipReason;
        var allowFailure = decision.AllowFailure;

        var job = new Job
        {
            Id = name,
            Name = name,
            Stage = stage,
            RunsOn = DefaultRunner,
            Container = ResolveImage(baseName, map["image"], Scope),
            ContainerOptional = true, // GitLab's shell executor ignores image:, so the host runner does too
            Variables = jobVariables,
            Matrix = instance.Matrix,
            Timeout = ReadTimeout(baseName, map["timeout"]),
            Condition = skipReason is not null
                ? new Condition { Expression = "false", Type = ConditionType.Expression, Description = skipReason }
                : decision.Condition
        };

        if (isTrigger)
        {
            job.Steps.Add(BuildTriggerStep(baseName, map["trigger"]));
        }
        else
        {
            var lines = new List<string>();
            lines.AddRange(GitLabYaml.ScriptLines(map["before_script"]));
            lines.AddRange(scriptLines);
            if (lines.Count > 0)
            {
                job.Steps.Add(new Step
                {
                    Id = "script",
                    Name = "script",
                    Type = StepType.Script,
                    Script = string.Join("\n", lines),
                    Shell = "bash",
                    ContinueOnError = allowFailure
                });
            }

            if (hasRun)
            {
                _ctx.Warn($"Job '{baseName}': the 'run' keyword (step definitions) is not supported locally; the step is skipped.");
                job.Steps.Add(new Step
                {
                    Id = "run",
                    Name = "run",
                    Type = StepType.Unknown,
                    ActionReference = "run",
                    ContinueOnError = allowFailure
                });
            }
        }

        var afterLines = GitLabYaml.ScriptLines(map["after_script"]);
        if (afterLines.Count > 0)
        {
            job.Steps.Add(new Step
            {
                Id = "after_script",
                Name = "after_script",
                Type = StepType.Script,
                Script = string.Join("\n", afterLines),
                Shell = "bash",
                Condition = new Condition { Expression = "always()", Type = ConditionType.Always },
                ContinueOnError = true
            });
        }

        var artifact = ReadArtifacts(baseName, name, map["artifacts"], Scope, out var artifactCondition);
        if (artifact is not null)
        {
            job.Steps.Add(new Step
            {
                Id = "artifacts",
                Name = "artifacts",
                Type = StepType.UploadArtifact,
                Artifact = artifact,
                Condition = artifactCondition,
                ContinueOnError = allowFailure
            });
        }

        return new JobEntry(baseName, map, instance, job, skipReason, artifact?.Name);
    }

    private string ReadStage(string name, GitLabMap map)
    {
        var stage = map["stage"] switch
        {
            null => DefaultStage,
            string s when !string.IsNullOrWhiteSpace(s) => s.Trim(),
            var other => throw Structure($"Job '{name}': 'stage' must be a stage name, found {GitLabYaml.Describe(other)}.", map, jobName: name)
        };

        if (!_stages.Contains(stage, StringComparer.Ordinal))
        {
            throw Structure(
                $"Job '{name}' uses stage '{stage}', which is not declared in 'stages'.",
                map,
                jobName: name,
                suggestions: new[]
                {
                    $"Declared stages: {string.Join(", ", _stages)}",
                    $"Add '{stage}' to the 'stages' list or change the job's 'stage'"
                });
        }

        return stage;
    }

    private Decision Decide(string baseName, string instanceName, GitLabMap map, Func<string, string?> scope)
    {
        var jobWhen = ReadWhen(baseName, map["when"]);
        var allowFailure = ReadAllowFailure(baseName, map);
        string? effectiveWhen;
        string? ruleText = null;
        var ruleVariables = new List<KeyValuePair<string, string>>();
        string? skipReason = null;

        if (map.ContainsKey("rules") && map["rules"] is not null)
        {
            if (map.ContainsKey("only") || map.ContainsKey("except"))
            {
                throw Structure($"Job '{baseName}' uses 'rules' together with 'only'/'except'; GitLab does not allow both.", map, jobName: baseName);
            }

            var outcome = EvaluateRules(map["rules"], scope, baseName);
            if (!outcome.Matched)
            {
                skipReason = "rules: no rule matched";
                effectiveWhen = null;
            }
            else
            {
                effectiveWhen = outcome.When ?? jobWhen ?? "on_success";
                allowFailure = outcome.AllowFailure ?? allowFailure;
                ruleVariables = outcome.Variables;
                ruleText = outcome.RuleText;
            }
        }
        else if (map.ContainsKey("only") || map.ContainsKey("except"))
        {
            var reason = EvaluateOnlyExcept(baseName, map["only"], map["except"], scope);
            skipReason = reason;
            effectiveWhen = reason is null ? jobWhen ?? "on_success" : null;
        }
        else
        {
            effectiveWhen = jobWhen ?? "on_success";
        }

        Condition? condition = null;
        if (skipReason is null)
        {
            var detail = ruleText is null ? string.Empty : $" ({ruleText})";
            switch (effectiveWhen)
            {
                case "never":
                    skipReason = $"when: never{detail}";
                    break;
                case "manual":
                    skipReason = $"manual job (when: manual{(ruleText is null ? string.Empty : ", " + ruleText)})";
                    if (allowFailure == false)
                    {
                        _ctx.Warn($"Job '{instanceName}' is a blocking manual job (allow_failure: false); on GitLab later stages wait for it, locally it is skipped and the pipeline continues.");
                    }

                    allowFailure ??= true;
                    break;
                case "delayed":
                    _ctx.Warn($"Job '{instanceName}' is delayed (when: delayed, start_in: {map["start_in"] as string ?? "unset"}); PDK runs it immediately.");
                    break;
                case "always":
                    condition = new Condition { Expression = "always()", Type = ConditionType.Always };
                    break;
                case "on_failure":
                    condition = new Condition { Expression = "failure()", Type = ConditionType.Failure };
                    break;
            }
        }

        return new Decision(skipReason, condition, allowFailure ?? false, ruleVariables);
    }

    private RuleOutcome EvaluateRules(object? rulesValue, Func<string, string?> scope, string owner)
    {
        if (rulesValue is not GitLabList rules)
        {
            throw Structure($"'{owner}': 'rules' must be a list of rules.", rulesValue as GitLabMap, jobName: owner == "workflow" ? null : owner);
        }

        for (var index = 0; index < rules.Count; index++)
        {
            var item = rules[index];
            if (item is not GitLabMap rule)
            {
                throw Structure($"'{owner}': rule #{index + 1} must be a mapping with 'if', 'exists', 'changes' or 'when'.", null, jobName: owner == "workflow" ? null : owner, line: rules.Line, column: rules.Column);
            }

            foreach (var key in rule.Keys)
            {
                if (!KnownRuleKeys.Contains(key))
                {
                    _ctx.Warn($"'{owner}': rule #{index + 1} uses '{key}', which is not a rules keyword and is ignored.");
                }
            }

            var matched = true;
            if (rule.ContainsKey("if") && rule["if"] is not null)
            {
                if (rule["if"] is not string expression)
                {
                    throw Structure($"'{owner}': rule #{index + 1} 'if' must be an expression string.", rule, jobName: owner == "workflow" ? null : owner);
                }

                matched = EvaluateExpression(expression, scope, owner, rule);
            }

            if (matched && rule.ContainsKey("exists") && rule["exists"] is not null)
            {
                matched = ExistsMatch(rule["exists"], scope, owner);
            }

            if (matched && rule.ContainsKey("changes") && rule["changes"] is not null)
            {
                _ctx.Debug($"'{owner}': rule #{index + 1} 'changes' is treated as matching (PDK does not compute a diff).");
            }

            if (!matched)
            {
                continue;
            }

            if (rule.ContainsKey("needs"))
            {
                _ctx.Debug($"'{owner}': rule #{index + 1} 'needs' override is ignored.");
            }

            return new RuleOutcome(
                true,
                ReadWhen(owner, rule["when"]),
                ReadAllowFailure(owner, rule),
                ReadVariables(rule["variables"], $"{owner}:rules:variables", out _),
                DescribeRule(rule, index));
        }

        return new RuleOutcome(false, null, null, new List<KeyValuePair<string, string>>(), null);
    }

    private static string DescribeRule(GitLabMap rule, int index)
    {
        var parts = new List<string>();
        if (rule["if"] is string condition)
        {
            parts.Add($"if: {condition.Trim()}");
        }

        if (rule.ContainsKey("exists"))
        {
            parts.Add($"exists: [{string.Join(", ", GitLabYaml.StringList(rule["exists"] is GitLabMap m ? m["paths"] : rule["exists"]))}]");
        }

        if (rule.ContainsKey("changes"))
        {
            parts.Add("changes: [...]");
        }

        return parts.Count == 0 ? $"rule #{index + 1} without conditions" : string.Join(", ", parts);
    }

    private bool EvaluateExpression(string expression, Func<string, string?> scope, string owner, GitLabMap position)
    {
        try
        {
            return new GitLabRulesEvaluator(scope).Evaluate(expression);
        }
        catch (GitLabExpressionException ex)
        {
            throw new PipelineParseException(
                ErrorCodes.InvalidPipelineStructure,
                $"'{owner}': invalid rules expression '{expression.Trim()}': {ex.Message}",
                ErrorContext.FromParserPosition(_ctx.DisplayPath, position.Line, position.Column).WithJob(owner),
                new[]
                {
                    "Rules compare variables with ==, !=, =~ /regex/ and !~, combined with && and || and parentheses",
                    "Example: if: $CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH && $CI_PIPELINE_SOURCE != \"schedule\""
                },
                ex);
        }
    }

    private bool ExistsMatch(object? value, Func<string, string?> scope, string owner)
    {
        List<string> patterns;
        if (value is GitLabMap map)
        {
            if (map.ContainsKey("project"))
            {
                _ctx.Warn($"'{owner}': 'exists:project' cannot be checked locally and is treated as matching.");
                return true;
            }

            patterns = GitLabYaml.StringList(map["paths"]);
        }
        else
        {
            patterns = GitLabYaml.StringList(value);
        }

        foreach (var raw in patterns)
        {
            var pattern = GitLabVariableExpander.Expand(raw, scope).Replace('\\', '/').Trim();
            if (pattern.Length == 0)
            {
                continue;
            }

            if (pattern.AsSpan().IndexOfAny(GlobCharacters) < 0)
            {
                var candidate = Path.Combine(_ctx.Workspace, pattern);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    return true;
                }

                continue;
            }

            try
            {
                var matcher = new Matcher(StringComparison.Ordinal);
                matcher.AddInclude(pattern);
                if (matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(_ctx.Workspace))).HasMatches)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _ctx.Debug($"'{owner}': could not evaluate exists pattern '{pattern}': {ex.Message}");
            }
        }

        return false;
    }

    private string? EvaluateOnlyExcept(string jobName, object? only, object? except, Func<string, string?> scope)
    {
        var refName = scope("CI_COMMIT_REF_NAME") ?? string.Empty;

        if (only is not null)
        {
            var spec = ReadOnlyExcept(jobName, only, "only");
            if (spec.Refs.Count > 0 && !RefsMatch(spec.Refs, scope))
            {
                return $"only: ref '{refName}' is not selected by only: [{string.Join(", ", spec.Refs)}]";
            }

            if (spec.Variables.Count > 0 && !spec.Variables.Any(expression => EvaluateExpression(expression, scope, jobName, spec.Position)))
            {
                return "only:variables: no expression matched";
            }

            if (spec.HasChanges)
            {
                _ctx.Debug($"Job '{jobName}': 'only:changes' is treated as matching (PDK does not compute a diff).");
            }
        }

        if (except is not null)
        {
            var spec = ReadOnlyExcept(jobName, except, "except");
            if (spec.Refs.Count > 0 && RefsMatch(spec.Refs, scope))
            {
                return $"except: ref '{refName}' is excluded by except: [{string.Join(", ", spec.Refs)}]";
            }

            if (spec.Variables.Count > 0 && spec.Variables.Any(expression => EvaluateExpression(expression, scope, jobName, spec.Position)))
            {
                return "except:variables: an expression matched";
            }

            if (spec.HasChanges)
            {
                _ctx.Debug($"Job '{jobName}': 'except:changes' is treated as matching (PDK does not compute a diff), so the job is excluded.");
                return "except:changes: changes are assumed (PDK cannot compute a diff)";
            }
        }

        return null;
    }

    private OnlyExceptSpec ReadOnlyExcept(string jobName, object value, string keyword)
    {
        switch (value)
        {
            case string single:
                return new OnlyExceptSpec(new List<string> { single.Trim() }, new List<string>(), false, new GitLabMap());
            case GitLabList list:
                return new OnlyExceptSpec(GitLabYaml.StringList(list), new List<string>(), false, new GitLabMap { Line = list.Line, Column = list.Column });
            case GitLabMap map:
            {
                foreach (var key in map.Keys)
                {
                    if (key is not ("refs" or "variables" or "changes" or "kubernetes"))
                    {
                        _ctx.Warn($"Job '{jobName}': '{keyword}:{key}' is not supported and is ignored.");
                    }
                }

                if (map.ContainsKey("kubernetes"))
                {
                    _ctx.Debug($"Job '{jobName}': '{keyword}:kubernetes' is treated as matching.");
                }

                return new OnlyExceptSpec(GitLabYaml.StringList(map["refs"]), GitLabYaml.StringList(map["variables"]), map.ContainsKey("changes"), map);
            }

            default:
                throw Structure($"Job '{jobName}': '{keyword}' must be a list of refs or a mapping with 'refs', 'variables' or 'changes'.", null, jobName: jobName);
        }
    }

    private bool RefsMatch(IEnumerable<string> refs, Func<string, string?> scope)
    {
        var source = scope("CI_PIPELINE_SOURCE") ?? "push";
        var refName = scope("CI_COMMIT_REF_NAME") ?? string.Empty;
        var branch = scope("CI_COMMIT_BRANCH") ?? string.Empty;
        var isBranch = source == "push" && (branch.Length > 0 || !_ctx.Git.IsRepository);

        foreach (var raw in refs)
        {
            var entry = raw.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (entry.StartsWith('/') && entry.LastIndexOf('/') > 0)
            {
                var close = entry.LastIndexOf('/');
                var pattern = entry[1..close];
                var flags = entry[(close + 1)..];
                try
                {
                    var regex = GitLabRulesEvaluator.ParseRegex(pattern, flags, entry);
                    if (regex.IsMatch(refName) || (branch.Length > 0 && regex.IsMatch(branch)))
                    {
                        return true;
                    }
                }
                catch (GitLabExpressionException ex)
                {
                    throw Structure($"Invalid ref pattern '{entry}' in only/except: {ex.Message}", null);
                }

                continue;
            }

            var at = entry.IndexOf('@');
            if (at > 0)
            {
                entry = entry[..at];
            }

            var matches = entry.ToLowerInvariant() switch
            {
                "branches" => isBranch,
                "tags" => false,
                "merge_requests" => source == "merge_request_event",
                "pushes" => source == "push",
                "web" => source == "web",
                "api" => source == "api",
                "schedules" => source == "schedule",
                "triggers" => source == "trigger",
                "pipelines" => source == "pipeline",
                "chat" => source == "chat",
                "external" => source == "external",
                "external_pull_requests" => source == "external_pull_request_event",
                _ => string.Equals(entry, refName, StringComparison.Ordinal) || (branch.Length > 0 && string.Equals(entry, branch, StringComparison.Ordinal))
            };

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private string? ReadWhen(string owner, object? value)
    {
        if (value is null)
        {
            return null;
        }

        var when = (value as string)?.Trim().ToLowerInvariant();
        return when switch
        {
            "on_success" or "on_failure" or "always" or "manual" or "never" or "delayed" => when,
            _ => throw Structure(
                $"'{owner}': 'when' must be one of on_success, on_failure, always, manual, never or delayed (found '{value}').",
                null,
                jobName: owner == "workflow" ? null : owner)
        };
    }

    private bool? ReadAllowFailure(string owner, GitLabMap map)
    {
        if (!map.ContainsKey("allow_failure"))
        {
            return null;
        }

        var value = map["allow_failure"];
        switch (value)
        {
            case null:
                return null;
            case string text when GitLabYaml.Bool(text) is { } flag:
                return flag;
            case GitLabMap detail when detail.ContainsKey("exit_codes"):
                _ctx.Warn($"'{owner}': 'allow_failure:exit_codes' is treated as 'allow_failure: true' (specific exit codes are not distinguished locally).");
                return true;
            default:
                throw Structure($"'{owner}': 'allow_failure' must be true, false or a mapping with 'exit_codes'.", null, jobName: owner == "workflow" ? null : owner);
        }
    }

    private TimeSpan? ReadTimeout(string jobName, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text && GitLabDuration.TryParse(text, out var duration) && duration > TimeSpan.Zero)
        {
            return duration;
        }

        _ctx.Warn($"Job '{jobName}': timeout '{value}' is not a duration GitLab understands (e.g. '1h 30m', '90 minutes'); no timeout is applied.");
        return null;
    }

    private string? ResolveImage(string jobName, object? value, Func<string, string?> scope)
    {
        string? image = null;
        switch (value)
        {
            case null:
                return null;
            case string text:
                image = text;
                break;
            case GitLabMap map:
                image = map["name"] as string;
                if (map.ContainsKey("entrypoint"))
                {
                    _ctx.Debug($"Job '{jobName}': 'image:entrypoint' is ignored; the image's own entrypoint is used.");
                }

                break;
            default:
                throw Structure($"Job '{jobName}': 'image' must be an image name or a mapping with 'name'.", null, jobName: jobName);
        }

        var expanded = GitLabVariableExpander.Expand(image, scope).Trim();
        return expanded.Length == 0 ? null : expanded;
    }

    private ArtifactDefinition? ReadArtifacts(string jobName, string instanceName, object? value, Func<string, string?> scope, out Condition? condition)
    {
        condition = null;
        if (value is null)
        {
            return null;
        }

        if (value is not GitLabMap artifacts)
        {
            throw Structure($"Job '{jobName}': 'artifacts' must be a mapping with 'paths'.", null, jobName: jobName);
        }

        if (artifacts.ContainsKey("reports"))
        {
            _ctx.Debug($"Job '{jobName}': 'artifacts:reports' is ignored (report artifacts are not collected locally).");
        }

        if (artifacts.ContainsKey("untracked") && GitLabYaml.Bool(artifacts["untracked"] as string) == true)
        {
            _ctx.Warn($"Job '{jobName}': 'artifacts:untracked' is not supported locally; only 'artifacts:paths' are collected.");
        }

        var patterns = GitLabYaml.StringList(artifacts["paths"])
            .Select(path => NormalizePattern(GitLabVariableExpander.Expand(path, scope)))
            .Where(path => path.Length > 0)
            .ToList();

        if (patterns.Count == 0)
        {
            return null;
        }

        patterns.AddRange(GitLabYaml.StringList(artifacts["exclude"])
            .Select(path => NormalizePattern(GitLabVariableExpander.Expand(path, scope)))
            .Where(path => path.Length > 0)
            .Select(path => "!" + path));

        var name = artifacts["name"] is string rawName && !string.IsNullOrWhiteSpace(rawName)
            ? GitLabVariableExpander.Expand(rawName, scope).Trim()
            : string.Empty;
        if (name.Length == 0)
        {
            name = instanceName;
        }

        int? retentionDays = null;
        if (artifacts["expire_in"] is string expireIn && !expireIn.Trim().Equals("never", StringComparison.OrdinalIgnoreCase))
        {
            if (GitLabDuration.TryParse(expireIn, out var expiry))
            {
                retentionDays = Math.Max(1, (int)Math.Ceiling(expiry.TotalDays));
            }
            else
            {
                _ctx.Warn($"Job '{jobName}': 'artifacts:expire_in' value '{expireIn}' is not a duration GitLab understands; the default retention applies.");
            }
        }

        var when = ReadWhen($"{jobName}:artifacts", artifacts["when"]) ?? "on_success";
        condition = when switch
        {
            "always" => new Condition { Expression = "always()", Type = ConditionType.Always },
            "on_failure" => new Condition { Expression = "failure()", Type = ConditionType.Failure },
            _ => null
        };

        return new ArtifactDefinition
        {
            Name = name,
            Operation = ArtifactOperation.Upload,
            Patterns = patterns.ToArray(),
            Options = new ArtifactOptions
            {
                IfNoFilesFound = IfNoFilesFound.Warn,
                Compression = CompressionType.Gzip,
                RetentionDays = retentionDays,
                OverwriteExisting = true
            }
        };
    }

    private static string NormalizePattern(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        while (normalized.Length > 1 && normalized.EndsWith('/'))
        {
            normalized = normalized[..^1];
        }

        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private Step BuildTriggerStep(string jobName, object? trigger)
    {
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        string target;
        switch (trigger)
        {
            case string project:
                target = project;
                inputs["project"] = project;
                break;
            case GitLabMap map:
                target = map["project"] as string ?? (map.ContainsKey("include") ? "child pipeline" : "downstream pipeline");
                foreach (var (key, value) in map)
                {
                    inputs[key] = value switch
                    {
                        string s => s,
                        null => string.Empty,
                        GitLabList list => string.Join("\n", GitLabYaml.StringList(list)),
                        _ => GitLabYaml.Describe(value)
                    };
                }

                break;
            default:
                target = "downstream pipeline";
                break;
        }

        _ctx.Warn($"Job '{jobName}' triggers '{target}'; downstream pipelines are not run locally and the job is skipped.");

        return new Step
        {
            Id = "trigger",
            Name = "Trigger downstream pipeline",
            Type = StepType.Unknown,
            ActionReference = "trigger",
            With = inputs
        };
    }

    private void NoteIgnoredJobKeys(string jobName, GitLabMap map)
    {
        if (map.ContainsKey("services") && map["services"] is not null)
        {
            _ctx.Warn($"Job '{jobName}': service containers ('services') are not supported locally and will be ignored.");
        }

        if (map.ContainsKey("release"))
        {
            _ctx.Warn($"Job '{jobName}': 'release' is not executed locally (no release is created).");
        }

        if (map.ContainsKey("secrets"))
        {
            _ctx.Warn($"Job '{jobName}': 'secrets' (external secret providers) are not available locally; use 'pdk secret set' to provide values.");
        }

        if (map.ContainsKey("id_tokens"))
        {
            _ctx.Warn($"Job '{jobName}': 'id_tokens' (OIDC tokens) are not issued locally.");
        }

        foreach (var key in new[] { "retry", "tags", "cache", "interruptible", "resource_group", "environment", "coverage", "hooks", "identity", "pages", "publish", "dast_configuration", "manual_confirmation" })
        {
            if (map.ContainsKey(key))
            {
                _ctx.Debug($"Job '{jobName}': '{key}' has no effect locally and is ignored.");
            }
        }
    }

    // ---------------------------------------------------------------- dependencies

    private void WireDependencies(List<JobEntry> entries)
    {
        var byBaseName = entries.GroupBy(e => e.BaseName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var byInstance = entries.ToDictionary(e => e.Job.Id, e => e, StringComparer.Ordinal);
        var needsByEntry = entries.ToDictionary(e => e, e => ReadNeeds(e, e.Map["needs"]));

        List<JobEntry> Resolve(JobEntry entry, string reference, string keyword, bool optional)
        {
            if (byBaseName.TryGetValue(reference, out var group))
            {
                return group;
            }

            if (byInstance.TryGetValue(reference, out var single))
            {
                return new List<JobEntry> { single };
            }

            if (optional)
            {
                _ctx.Debug($"Job '{entry.Job.Id}': optional need '{reference}' is not defined and is ignored.");
                return new List<JobEntry>();
            }

            if (_templates.ContainsKey(reference))
            {
                throw MissingDependency(entry, reference, keyword, $"'{reference}' is a hidden job (its name starts with '.') and is never run");
            }

            throw MissingDependency(entry, reference, keyword, null);
        }

        // A job that needs a skipped job (manual, never, unmatched rules) never runs on GitLab either;
        // propagate until stable so chains of needs are handled regardless of declaration order.
        bool changed;
        do
        {
            changed = false;
            foreach (var entry in entries)
            {
                if (entry.SkipReason is not null || needsByEntry[entry] is not { } needs)
                {
                    continue;
                }

                foreach (var need in needs)
                {
                    var blocker = Resolve(entry, need.Job, "needs", need.Optional)
                        .FirstOrDefault(target => !ReferenceEquals(target, entry) && target.SkipReason is not null && !need.Optional);
                    if (blocker is not null)
                    {
                        entry.MarkSkipped($"needs '{blocker.Job.Id}', which is skipped ({blocker.SkipReason})");
                        changed = true;
                        break;
                    }
                }
            }
        }
        while (changed);

        foreach (var entry in entries)
        {
            var map = entry.Map;
            var stageIndex = _stages.IndexOf(entry.Job.Stage!);
            var dependsOn = new List<string>();
            var downloads = new List<JobEntry>();

            var needs = needsByEntry[entry];
            if (needs is not null)
            {
                foreach (var need in needs)
                {
                    foreach (var target in Resolve(entry, need.Job, "needs", need.Optional))
                    {
                        if (ReferenceEquals(target, entry))
                        {
                            throw new PipelineParseException(
                                ErrorCodes.SelfDependency,
                                $"Job '{entry.Job.Id}' needs itself.",
                                new ErrorContext { PipelineFile = _ctx.DisplayPath }.WithJob(entry.Job.Id),
                                new[] { "Remove the job's own name from its 'needs' list" });
                        }

                        if (target.SkipReason is not null)
                        {
                            continue;
                        }

                        dependsOn.Add(target.Job.Id);
                        if (need.Artifacts && target.ArtifactName is not null)
                        {
                            downloads.Add(target);
                        }
                    }
                }
            }
            else
            {
                foreach (var earlier in entries)
                {
                    if (earlier.SkipReason is null && _stages.IndexOf(earlier.Job.Stage!) < stageIndex)
                    {
                        dependsOn.Add(earlier.Job.Id);
                        if (earlier.ArtifactName is not null)
                        {
                            downloads.Add(earlier);
                        }
                    }
                }
            }

            if (map.ContainsKey("dependencies") && map["dependencies"] is not null)
            {
                if (map["dependencies"] is not GitLabList dependencyList)
                {
                    throw Structure($"Job '{entry.BaseName}': 'dependencies' must be a list of job names.", map, jobName: entry.BaseName);
                }

                downloads.Clear();
                foreach (var dependency in GitLabYaml.StringList(dependencyList))
                {
                    foreach (var target in Resolve(entry, dependency, "dependencies", optional: false))
                    {
                        if (ReferenceEquals(target, entry) || target.SkipReason is not null)
                        {
                            continue;
                        }

                        if (!dependsOn.Contains(target.Job.Id, StringComparer.Ordinal))
                        {
                            dependsOn.Add(target.Job.Id);
                        }

                        if (target.ArtifactName is not null)
                        {
                            downloads.Add(target);
                        }
                    }
                }
            }

            entry.Job.DependsOn = dependsOn.Distinct(StringComparer.Ordinal).ToList();

            var downloadSteps = downloads
                .GroupBy(d => d.ArtifactName!, StringComparer.Ordinal)
                .Select(group => new Step
                {
                    Id = "download:" + group.First().Job.Id,
                    Name = $"Download artifacts from {string.Join(", ", group.Select(d => d.Job.Id))}",
                    Type = StepType.DownloadArtifact,
                    Artifact = new ArtifactDefinition
                    {
                        Name = group.Key,
                        Operation = ArtifactOperation.Download,
                        Patterns = Array.Empty<string>(),
                        Options = ArtifactOptions.Default
                    },
                    ContinueOnError = true
                })
                .ToList();

            if (downloadSteps.Count > 0)
            {
                entry.Job.Steps.InsertRange(0, downloadSteps);
            }
        }
    }

    private List<Need>? ReadNeeds(JobEntry entry, object? value)
    {
        if (!entry.Map.ContainsKey("needs"))
        {
            return null;
        }

        var result = new List<Need>();
        switch (value)
        {
            case null:
                return result;
            case string single:
                result.Add(new Need(single.Trim(), true, false));
                return result;
            case GitLabList list:
                foreach (var item in list)
                {
                    switch (item)
                    {
                        case string name when !string.IsNullOrWhiteSpace(name):
                            result.Add(new Need(name.Trim(), true, false));
                            break;
                        case GitLabMap need:
                            if (need.ContainsKey("pipeline") || need.ContainsKey("project"))
                            {
                                _ctx.Warn($"Job '{entry.Job.Id}': cross-pipeline/cross-project 'needs' entries are not supported locally and are ignored.");
                                break;
                            }

                            if (need["job"] is not string jobName || string.IsNullOrWhiteSpace(jobName))
                            {
                                throw Structure($"Job '{entry.BaseName}': each 'needs' entry must be a job name or a mapping with 'job'.", need, jobName: entry.BaseName);
                            }

                            if (need.ContainsKey("parallel"))
                            {
                                _ctx.Debug($"Job '{entry.Job.Id}': 'needs:parallel:matrix' selection is not supported; the job depends on every instance of '{jobName}'.");
                            }

                            result.Add(new Need(
                                jobName.Trim(),
                                GitLabYaml.Bool(need["artifacts"] as string) ?? true,
                                GitLabYaml.Bool(need["optional"] as string) ?? false));
                            break;
                        default:
                            throw Structure($"Job '{entry.BaseName}': each 'needs' entry must be a job name or a mapping with 'job'.", entry.Map, jobName: entry.BaseName);
                    }
                }

                return result;
            default:
                throw Structure($"Job '{entry.BaseName}': 'needs' must be a list of job names.", entry.Map, jobName: entry.BaseName);
        }
    }

    private void DetectCycles(List<JobEntry> entries)
    {
        var graph = entries.ToDictionary(e => e.Job.Id, e => e.Job.DependsOn, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var id in graph.Keys)
        {
            var cycle = FindCycle(id, graph, visited, stack);
            if (cycle is not null)
            {
                throw PipelineParseException.CircularDependency(_ctx.DisplayPath, cycle);
            }
        }
    }

    private static List<string>? FindCycle(string id, Dictionary<string, List<string>> graph, HashSet<string> visited, List<string> stack)
    {
        var index = stack.IndexOf(id);
        if (index >= 0)
        {
            var cycle = stack.Skip(index).ToList();
            cycle.Add(id);
            return cycle;
        }

        if (!visited.Add(id))
        {
            return null;
        }

        stack.Add(id);
        if (graph.TryGetValue(id, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                var cycle = FindCycle(dependency, graph, visited, stack);
                if (cycle is not null)
                {
                    return cycle;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        return null;
    }

    // ---------------------------------------------------------------- helpers

    private List<KeyValuePair<string, string>> ReadVariables(object? value, string context, out HashSet<string> noExpand)
    {
        noExpand = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<KeyValuePair<string, string>>();
        if (value is null)
        {
            return result;
        }

        if (value is not GitLabMap map)
        {
            throw Structure($"'{context}' must be a mapping of variable names to values.", null);
        }

        foreach (var (name, entry) in map)
        {
            switch (entry)
            {
                case null:
                    result.Add(new KeyValuePair<string, string>(name, string.Empty));
                    break;
                case string text:
                    result.Add(new KeyValuePair<string, string>(name, text));
                    break;
                case GitLabMap detail:
                {
                    var variableValue = detail["value"] switch
                    {
                        string s => s,
                        null => string.Empty,
                        var other => throw Structure($"'{context}:{name}:value' must be a scalar, found {GitLabYaml.Describe(other)}.", detail)
                    };

                    if (GitLabYaml.Bool(detail["expand"] as string) == false)
                    {
                        noExpand.Add(name);
                    }

                    result.Add(new KeyValuePair<string, string>(name, variableValue));
                    break;
                }

                default:
                    throw Structure($"'{context}:{name}' must be a scalar or a mapping with 'value', found {GitLabYaml.Describe(entry)}.", map);
            }
        }

        return result;
    }

    private Dictionary<string, string> BuildPredefined(Job? job, int jobNumber)
    {
        return GitLabPredefinedVariables.Build(new GitLabVariableContext
        {
            Git = _ctx.Git,
            Workspace = _ctx.Workspace,
            EventName = _ctx.Options.EventName,
            DefaultBranch = null,
            PipelineName = _pipelineName,
            RunId = "1",
            Job = job,
            JobNumber = jobNumber
        });
    }

    private PipelineParseException MissingDependency(JobEntry entry, string reference, string keyword, string? detail)
    {
        var message = $"Job '{entry.Job.Id}' {(keyword == "needs" ? "needs" : "depends on")} '{reference}', which is not defined in the pipeline" +
                      (detail is null ? "." : $": {detail}.");

        return new PipelineParseException(
            ErrorCodes.MissingDependency,
            message,
            new ErrorContext { PipelineFile = _ctx.DisplayPath, LineNumber = entry.Map.Line > 0 ? entry.Map.Line : null }.WithJob(entry.Job.Id),
            new[]
            {
                $"Define a job named '{reference}' or remove it from the '{keyword}' list of '{entry.BaseName}'",
                keyword == "needs" ? "Use 'needs: [{ job: name, optional: true }]' when the job may be absent from the pipeline" : "'dependencies' entries must name jobs from earlier stages"
            });
    }

    private PipelineParseException Structure(
        string message,
        GitLabMap? position,
        string? jobName = null,
        IEnumerable<string>? suggestions = null,
        int line = 0,
        int column = 0)
    {
        var errorLine = position?.Line ?? line;
        var errorColumn = position?.Column ?? column;
        var context = errorLine > 0
            ? ErrorContext.FromParserPosition(_ctx.DisplayPath, errorLine, errorColumn)
            : new ErrorContext { PipelineFile = _ctx.DisplayPath };

        if (jobName is not null)
        {
            context = context.WithJob(jobName);
        }

        var location = errorLine > 0 ? $" (line {errorLine})" : string.Empty;
        return new PipelineParseException(ErrorCodes.InvalidPipelineStructure, $"{message}{location}", context, suggestions);
    }

    private sealed record JobInstance(string Name, Dictionary<string, string> Variables, Dictionary<string, string>? Matrix);

    private sealed record Need(string Job, bool Artifacts, bool Optional);

    private sealed record RuleOutcome(bool Matched, string? When, bool? AllowFailure, List<KeyValuePair<string, string>> Variables, string? RuleText);

    private sealed record Decision(string? SkipReason, Condition? Condition, bool AllowFailure, List<KeyValuePair<string, string>> RuleVariables);

    private sealed record OnlyExceptSpec(List<string> Refs, List<string> Variables, bool HasChanges, GitLabMap Position);

    private sealed class JobEntry
    {
        public JobEntry(string baseName, GitLabMap map, JobInstance instance, Job job, string? skipReason, string? artifactName)
        {
            BaseName = baseName;
            Map = map;
            Instance = instance;
            Job = job;
            SkipReason = skipReason;
            ArtifactName = artifactName;
        }

        public string BaseName { get; }

        public GitLabMap Map { get; }

        public JobInstance Instance { get; }

        public Job Job { get; }

        public string? SkipReason { get; private set; }

        public string? ArtifactName { get; }

        public void MarkSkipped(string reason)
        {
            SkipReason = reason;
            Job.Condition = new Condition { Expression = "false", Type = ConditionType.Expression, Description = reason };
        }
    }
}
