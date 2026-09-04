using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers.Common;
using PDK.Providers.GitHub.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PDK.Providers.GitHub;

/// <summary>
/// Parser for GitHub Actions workflow files.
/// Converts GitHub Actions YAML into the common PDK Pipeline model.
/// </summary>
public class GitHubActionsParser : IPipelineParser, IPipelineParserWarnings
{
    private const string InlineContentName = "workflow";
    private const string DefaultRunner = "ubuntu-latest";

    private static readonly Regex TopLevelJobsKey = new(@"^jobs\s*:", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TopLevelOnKey = new(@"^(?:on|'on'|""on"")\s*:", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly ILogger<GitHubActionsParser>? _logger;
    private readonly IDeserializer _yamlDeserializer;
    private List<string> _warnings = new();

    /// <summary>
    /// Initializes a new instance of the GitHubActionsParser.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public GitHubActionsParser(ILogger<GitHubActionsParser>? logger = null)
    {
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .WithNodeDeserializer(new ScalarToListNodeDeserializer(), where => where.OnTop())
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Parses a GitHub Actions workflow YAML content into a Pipeline model.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <returns>A Pipeline object representing the workflow.</returns>
    /// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
    public Pipeline Parse(string yamlContent) => ParseCore(yamlContent, null);

    /// <summary>
    /// Parses a GitHub Actions workflow file into a Pipeline model.
    /// </summary>
    /// <param name="filePath">Path to the workflow YAML file.</param>
    /// <returns>A Pipeline object representing the workflow.</returns>
    /// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
    public async Task<Pipeline> ParseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new PipelineParseException("File path is empty or null");
        }

        if (!File.Exists(filePath))
        {
            throw new PipelineParseException($"File not found: {filePath}");
        }

        _logger?.LogDebug("Reading workflow file: {FilePath}", filePath);

        string yamlContent;
        try
        {
            yamlContent = await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to read file: {FilePath}", filePath);
            throw new PipelineParseException($"Failed to read file '{filePath}': {ex.Message}", ex);
        }

        return ParseCore(yamlContent, filePath);
    }

    /// <summary>
    /// Determines if this parser can parse the given file: it must have a top-level <c>jobs:</c> mapping and either
    /// an <c>on:</c> trigger or a job with <c>runs-on</c> (or a reusable-workflow <c>uses</c>).
    /// </summary>
    /// <param name="filePath">Path to the file to check.</param>
    /// <returns>True if this parser can handle the file; otherwise, false.</returns>
    public bool CanParse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(filePath);

            // Must have a top-level 'jobs' section
            if (!TopLevelJobsKey.IsMatch(content))
            {
                return false;
            }

            // Must deserialize as a GitHub workflow (Azure 'jobs:' is a list and fails here)
            var workflow = _yamlDeserializer.Deserialize<GitHubWorkflow>(content);
            if (workflow?.Jobs is null || workflow.Jobs.Count == 0)
            {
                return false;
            }

            var hasTrigger = workflow.On is not null || TopLevelOnKey.IsMatch(content);
            var hasRunner = workflow.Jobs.Values.Any(job =>
                job is not null && (RunsOnResolver.Resolve(job.RunsOn) is not null || job.IsReusableWorkflow));

            var result = hasTrigger || hasRunner;

            _logger?.LogDebug("CanParse result for {FilePath}: {Result} (Has on: {HasTrigger}, Has runs-on: {HasRunner})",
                filePath, result, hasTrigger, hasRunner);

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "CanParse failed for {FilePath}", filePath);
            return false;
        }
    }

    private Pipeline ParseCore(string yamlContent, string? filePath)
    {
        _warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new PipelineParseException("YAML content is empty or null");
        }

        var displayPath = filePath ?? InlineContentName;

        _logger?.LogDebug("Starting GitHub Actions workflow parsing");

        GitHubWorkflow? workflow;
        try
        {
            workflow = _yamlDeserializer.Deserialize<GitHubWorkflow>(yamlContent);
        }
        catch (YamlException ex)
        {
            _logger?.LogError(ex, "Failed to deserialize YAML content");
            throw YamlErrorTranslator.Translate(ex, yamlContent, displayPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during YAML deserialization");
            throw new PipelineParseException($"Failed to parse YAML: {ex.Message}", ex);
        }

        // Validate the workflow structure
        ValidateWorkflow(workflow, displayPath);

        // Convert to Pipeline model
        var pipeline = ConvertToPipeline(workflow!, displayPath);

        foreach (var warning in _warnings)
        {
            _logger?.LogWarning("{ParserWarning}", warning);
        }

        _logger?.LogInformation("Successfully parsed GitHub Actions workflow: {WorkflowName}", pipeline.Name);

        return pipeline;
    }

    /// <summary>
    /// Validates the workflow structure and throws if invalid.
    /// </summary>
    private static void ValidateWorkflow(GitHubWorkflow? workflow, string displayPath)
    {
        // Must have at least one job
        if (workflow?.Jobs is null || workflow.Jobs.Count == 0)
        {
            throw StructureError(
                "Workflow must contain at least one job in the 'jobs' section.",
                displayPath,
                suggestions: new[]
                {
                    "Add a 'jobs' mapping with at least one job",
                    "Example: jobs: { build: { runs-on: ubuntu-latest, steps: [{ run: 'echo Hello' }] } }"
                });
        }

        // Validate each job
        foreach (var (jobId, job) in workflow.Jobs)
        {
            if (job is null)
            {
                throw StructureError(
                    $"Job '{jobId}' is empty. A job must define 'runs-on' and 'steps' (or 'uses' for a reusable workflow).",
                    displayPath,
                    jobId,
                    suggestions: new[] { "Indent the job body under the job id", "Example: build:\n    runs-on: ubuntu-latest\n    steps: [...]" });
            }

            if (job.IsReusableWorkflow)
            {
                continue;
            }

            // Must have runs-on
            if (RunsOnResolver.Resolve(job.RunsOn) is null)
            {
                throw PipelineParseException.MissingRequiredField(displayPath, "runs-on", jobId);
            }

            // Must have at least one step
            if (job.Steps is null || job.Steps.Count == 0)
            {
                throw StructureError(
                    $"Job '{jobId}' must contain at least one step.",
                    displayPath,
                    jobId,
                    suggestions: new[] { "Add a 'steps' list with at least one 'run' or 'uses' entry" });
            }

            // Validate each step
            for (var i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var stepLabel = $"step {i + 1}";

                if (step is null)
                {
                    throw StructureError(
                        $"Job '{jobId}', {stepLabel}: step is empty. Each step must specify either 'uses' (action) or 'run' (command).",
                        displayPath,
                        jobId,
                        stepLabel);
                }

                // Must have either 'uses' or 'run', but not both or neither
                var hasUses = !string.IsNullOrWhiteSpace(step.Uses);
                var hasRun = !string.IsNullOrWhiteSpace(step.Run);

                if (!hasUses && !hasRun)
                {
                    throw StructureError(
                        $"Job '{jobId}', {stepLabel}: Must specify either 'uses' (action) or 'run' (command).",
                        displayPath,
                        jobId,
                        step.Name ?? stepLabel);
                }

                if (hasUses && hasRun)
                {
                    throw StructureError(
                        $"Job '{jobId}', {stepLabel}: Cannot specify both 'uses' and 'run'. Choose one.",
                        displayPath,
                        jobId,
                        step.Name ?? stepLabel);
                }
            }
        }

        // Validate job dependencies (check for circular references)
        ValidateJobDependencies(workflow, displayPath);
    }

    /// <summary>
    /// Validates job dependencies for missing targets and circular references.
    /// </summary>
    private static void ValidateJobDependencies(GitHubWorkflow workflow, string displayPath)
    {
        var jobGraph = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // Build dependency graph
        foreach (var (jobId, job) in workflow.Jobs!)
        {
            var dependencies = ActionMapper.ParseJobDependencies(job?.Needs);

            // Validate that all dependencies exist
            foreach (var dep in dependencies)
            {
                if (!workflow.Jobs.ContainsKey(dep))
                {
                    throw StructureError(
                        $"Job '{jobId}' depends on job '{dep}' which does not exist in the workflow.",
                        displayPath,
                        jobId,
                        suggestions: new[] { $"Remove '{dep}' from the 'needs' list of job '{jobId}' or add a job with that id" });
                }
            }

            jobGraph[jobId] = dependencies;
        }

        // Check for circular dependencies using DFS
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var jobId in jobGraph.Keys)
        {
            var cycle = FindCycle(jobId, jobGraph, visited, stack);
            if (cycle is not null)
            {
                throw PipelineParseException.CircularDependency(displayPath, cycle);
            }
        }
    }

    /// <summary>
    /// Detects circular dependencies using depth-first search; returns the cycle path when one is found.
    /// </summary>
    private static List<string>? FindCycle(
        string jobId,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        List<string> stack)
    {
        var index = stack.IndexOf(jobId);
        if (index >= 0)
        {
            var cycle = stack.Skip(index).ToList();
            cycle.Add(jobId);
            return cycle;
        }

        if (!visited.Add(jobId))
        {
            return null; // Already fully processed
        }

        stack.Add(jobId);

        if (graph.TryGetValue(jobId, out var dependencies))
        {
            foreach (var dep in dependencies)
            {
                var cycle = FindCycle(dep, graph, visited, stack);
                if (cycle is not null)
                {
                    return cycle;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        return null;
    }

    /// <summary>
    /// Converts a GitHubWorkflow to a PDK Pipeline model, expanding matrix jobs and rewriting 'needs'.
    /// </summary>
    private Pipeline ConvertToPipeline(GitHubWorkflow workflow, string displayPath)
    {
        var pipeline = new Pipeline
        {
            Name = workflow.Name ?? "Unnamed Workflow",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>(),
            Variables = ActionMapper.MergeEnvironmentVariables(workflow.Env, null, null)
        };

        var workflowDefaults = workflow.Defaults?.Run;

        // Pass 1: decide how each job expands so ids can be reserved before matrix ids are generated
        var plans = new List<JobPlan>();
        var reservedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (jobId, gitHubJob) in workflow.Jobs!)
        {
            var combinations = gitHubJob.IsReusableWorkflow
                ? Array.Empty<Dictionary<string, string>>()
                : MatrixExpander.Expand(gitHubJob.Strategy?.Matrix, _warnings, jobId);

            plans.Add(new JobPlan(jobId, gitHubJob, combinations));

            if (combinations.Count == 0)
            {
                reservedIds.Add(jobId);
            }
        }

        // Pass 2: convert jobs
        var expandedIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var converted = new List<(Job Job, List<string> Needs)>();

        foreach (var plan in plans)
        {
            var needs = ActionMapper.ParseJobDependencies(plan.Job.Needs);
            var ids = new List<string>();

            if (plan.Job.IsReusableWorkflow)
            {
                converted.Add((ConvertReusableWorkflowJob(plan.Id, plan.Job, workflow.Env), needs));
                ids.Add(plan.Id);
            }
            else if (plan.Combinations.Count == 0)
            {
                var runDefaults = GitHubRunDefaults.Merge(workflowDefaults, plan.Job.Defaults?.Run);
                converted.Add((ConvertToJob(plan.Id, plan.Job, workflow.Env, runDefaults, null), needs));
                ids.Add(plan.Id);
            }
            else
            {
                foreach (var combination in plan.Combinations)
                {
                    var expandedId = UniqueJobId(MatrixExpander.BuildJobId(plan.Id, combination), reservedIds);
                    var substituted = MatrixExpander.SubstituteJob(plan.Job, combination);
                    var runDefaults = MatrixExpander.SubstituteRunDefaults(
                        GitHubRunDefaults.Merge(workflowDefaults, substituted.Defaults?.Run),
                        combination);

                    var job = ConvertToJob(expandedId, substituted, workflow.Env, runDefaults, combination);
                    job.Name = MatrixExpander.BuildDisplayName(plan.Job.Name, plan.Id, combination);

                    converted.Add((job, needs));
                    ids.Add(expandedId);
                }
            }

            expandedIds[plan.Id] = ids;
        }

        // Rewrite 'needs' so a dependency on a matrix job targets every expanded instance
        foreach (var (job, needs) in converted)
        {
            job.DependsOn = needs
                .SelectMany(need => expandedIds.TryGetValue(need, out var targets) ? targets : new List<string> { need })
                .Distinct(StringComparer.Ordinal)
                .ToList();

            pipeline.Jobs[job.Id] = job;
        }

        _logger?.LogDebug("Converted workflow {DisplayPath} into {JobCount} job(s)", displayPath, pipeline.Jobs.Count);

        return pipeline;
    }

    /// <summary>
    /// Converts a GitHubJob to a PDK Job model.
    /// </summary>
    private Job ConvertToJob(
        string jobId,
        GitHubJob gitHubJob,
        Dictionary<string, string>? workflowEnv,
        GitHubRunDefaults? runDefaults,
        Dictionary<string, string>? matrix)
    {
        var job = new Job
        {
            Id = jobId,
            Name = gitHubJob.Name ?? jobId,
            RunsOn = RunsOnResolver.Resolve(gitHubJob.RunsOn) ?? DefaultRunner,
            Steps = new List<Step>(),
            Environment = ActionMapper.MergeEnvironmentVariables(workflowEnv, gitHubJob.Env, null),
            Matrix = matrix,
            Container = ResolveContainer(gitHubJob.Container)
        };

        // Set timeout if specified as a literal (expressions cannot be evaluated at parse time)
        if (gitHubJob.TimeoutMinutesValue is int timeoutMinutes)
        {
            job.Timeout = TimeSpan.FromMinutes(timeoutMinutes);
        }

        // Set condition if specified (kept raw for the expression engine)
        if (!string.IsNullOrWhiteSpace(gitHubJob.If))
        {
            job.Condition = new Condition
            {
                Expression = gitHubJob.If,
                Type = ConditionType.Expression
            };
        }

        if (gitHubJob.Services is not null)
        {
            _warnings.Add($"Job '{jobId}': service containers ('services') are not supported locally and will be ignored.");
        }

        // Convert each step
        var steps = gitHubJob.Steps ?? new List<GitHubStep>();
        for (var i = 0; i < steps.Count; i++)
        {
            var gitHubStep = steps[i];
            var step = ActionMapper.MapStep(gitHubStep, i, runDefaults);

            // Merge environment variables (workflow -> job -> step)
            step.Environment = ActionMapper.MergeEnvironmentVariables(
                workflowEnv,
                gitHubJob.Env,
                gitHubStep.Env);

            job.Steps.Add(step);
        }

        return job;
    }

    /// <summary>
    /// Converts a reusable-workflow job (<c>uses:</c> at job level) into a single unknown step the runners skip.
    /// </summary>
    private Job ConvertReusableWorkflowJob(string jobId, GitHubJob gitHubJob, Dictionary<string, string>? workflowEnv)
    {
        var workflowRef = gitHubJob.Uses!.Trim();

        var job = new Job
        {
            Id = jobId,
            Name = gitHubJob.Name ?? jobId,
            RunsOn = DefaultRunner,
            Steps = new List<Step>(),
            Environment = ActionMapper.MergeEnvironmentVariables(workflowEnv, gitHubJob.Env, null)
        };

        if (!string.IsNullOrWhiteSpace(gitHubJob.If))
        {
            job.Condition = new Condition
            {
                Expression = gitHubJob.If,
                Type = ConditionType.Expression
            };
        }

        var inputs = new Dictionary<string, string>();
        if (gitHubJob.With is not null)
        {
            foreach (var input in gitHubJob.With)
            {
                inputs[input.Key] = YamlValues.AsString(input.Value) ?? string.Empty;
            }
        }

        inputs["_action"] = workflowRef;

        job.Steps.Add(new Step
        {
            Name = $"Reusable workflow {workflowRef}",
            Type = StepType.Unknown,
            ActionReference = workflowRef,
            With = inputs,
            Environment = new Dictionary<string, string>(job.Environment)
        });

        _warnings.Add($"Job '{jobId}' calls reusable workflow '{workflowRef}'; reusable workflows are not executed locally and the job will be skipped.");

        return job;
    }

    private static string? ResolveContainer(object? container)
    {
        var image = container switch
        {
            string text => text,
            IDictionary<object, object> mapping => YamlValues.AsString(YamlValues.GetValue(mapping, "image")),
            _ => null
        };

        return string.IsNullOrWhiteSpace(image) ? null : image.Trim();
    }

    private static string UniqueJobId(string candidate, HashSet<string> reservedIds)
    {
        if (reservedIds.Add(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (!reservedIds.Add($"{candidate}-{suffix}"))
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }

    private static PipelineParseException StructureError(
        string message,
        string displayPath,
        string? jobId = null,
        string? stepName = null,
        IEnumerable<string>? suggestions = null)
    {
        var context = new ErrorContext { PipelineFile = displayPath };
        if (jobId is not null)
        {
            context = context.WithJob(jobId);
        }

        if (stepName is not null)
        {
            context = context.WithStep(stepName);
        }

        return new PipelineParseException(ErrorCodes.InvalidPipelineStructure, message, context, suggestions);
    }

    private sealed record JobPlan(string Id, GitHubJob Job, IReadOnlyList<Dictionary<string, string>> Combinations);
}
