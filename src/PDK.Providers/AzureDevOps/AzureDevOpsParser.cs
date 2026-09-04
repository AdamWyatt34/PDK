using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps.Models;
using PDK.Providers.Common;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// Parses Azure DevOps Pipeline YAML files into the common PDK Pipeline model.
/// Supports multi-stage pipelines, single-stage pipelines, and simple pipelines.
/// </summary>
public class AzureDevOpsParser : IPipelineParser, IPipelineParserWarnings
{
    private const string InlineContentName = "pipeline";
    private const string DefaultRunner = "ubuntu-latest";
    private const string SelfHostedRunner = "self-hosted";

    private static readonly Regex AzureTopLevelKey = new(
        @"^(?:steps|jobs|stages|pool|trigger|pr|extends|resources|variables|parameters|schedules)\s*:",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex GitHubOnKey = new(
        @"^(?:on|'on'|""on"")\s*:",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex GitHubJobsKey = new(@"^jobs\s*:", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex GitHubRunsOnKey = new(@"^\s*runs-on\s*:", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex TemplateExpressionKey = new(
        @"^(?<indent>[ \t]*-?[ \t]*)\$\{\{\s*(?<kind>if|elseif|else|each|insert)\b[^}]*\}\}\s*:",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly ILogger<AzureDevOpsParser>? _logger;
    private readonly IDeserializer _yamlDeserializer;
    private List<string> _warnings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureDevOpsParser"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    public AzureDevOpsParser(ILogger<AzureDevOpsParser>? logger = null)
    {
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithNodeDeserializer(new AzurePoolNodeDeserializer(), where => where.OnTop())
            .WithNodeDeserializer(new ScalarToListNodeDeserializer(), where => where.OnTop())
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Determines whether this parser can parse the specified file: a <c>.yml</c>/<c>.yaml</c> file whose top level
    /// contains an Azure key (<c>steps</c>, <c>jobs</c>, <c>stages</c>, <c>pool</c>, <c>trigger</c>, <c>pr</c>, ...)
    /// and that is not shaped like a GitHub workflow (<c>on:</c> + <c>jobs:</c>, or <c>runs-on</c>).
    /// </summary>
    /// <param name="filePath">The path to the pipeline file.</param>
    /// <returns>True if this parser can handle the file; otherwise, false.</returns>
    public bool CanParse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            // Check file extension
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension != ".yml" && extension != ".yaml")
            {
                return false;
            }

            var content = File.ReadAllText(filePath);

            if (!AzureTopLevelKey.IsMatch(content) || LooksLikeGitHubWorkflow(content))
            {
                return false;
            }

            // Must deserialize into the Azure model (a GitHub 'jobs' mapping fails here)
            var pipeline = _yamlDeserializer.Deserialize<AzurePipeline>(content);
            var result = pipeline is not null;

            _logger?.LogDebug("CanParse result for {FilePath}: {Result}", filePath, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "CanParse failed for {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Parses Azure Pipeline YAML content into a common PDK Pipeline model.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <returns>A Pipeline object representing the parsed Azure Pipeline.</returns>
    /// <exception cref="PipelineParseException">Thrown when the YAML content is invalid or cannot be parsed.</exception>
    public Pipeline Parse(string yamlContent) => ParseCore(yamlContent, null);

    /// <summary>
    /// Parses an Azure Pipeline YAML file into a common PDK Pipeline model.
    /// </summary>
    /// <param name="filePath">The path to the Azure Pipeline YAML file.</param>
    /// <returns>A Task that resolves to a Pipeline object.</returns>
    /// <exception cref="PipelineParseException">Thrown when the file cannot be read or parsed.</exception>
    public async Task<Pipeline> ParseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new PipelineParseException("File path cannot be null or empty");
        }

        if (!File.Exists(filePath))
        {
            throw new PipelineParseException($"File not found: {filePath}");
        }

        _logger?.LogDebug("Reading Azure Pipeline file: {FilePath}", filePath);

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

    private static bool LooksLikeGitHubWorkflow(string content) =>
        (GitHubOnKey.IsMatch(content) && GitHubJobsKey.IsMatch(content)) || GitHubRunsOnKey.IsMatch(content);

    private Pipeline ParseCore(string yamlContent, string? filePath)
    {
        _warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new PipelineParseException("YAML content cannot be null or empty");
        }

        var displayPath = filePath ?? InlineContentName;

        _logger?.LogDebug("Starting Azure Pipeline parsing");

        RejectTemplateExpressions(yamlContent, displayPath);

        AzurePipeline? azurePipeline;
        try
        {
            azurePipeline = _yamlDeserializer.Deserialize<AzurePipeline>(yamlContent);
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

        if (azurePipeline == null)
        {
            throw HierarchyError(displayPath);
        }

        // Validate the pipeline structure
        ValidatePipeline(azurePipeline, displayPath);

        // Convert to common Pipeline model
        var pipeline = ConvertToPipeline(azurePipeline);

        foreach (var warning in _warnings)
        {
            _logger?.LogWarning("{ParserWarning}", warning);
        }

        _logger?.LogInformation("Successfully parsed Azure Pipeline: {PipelineName}", pipeline.Name);

        return pipeline;
    }

    /// <summary>
    /// Rejects <c>${{ if }}</c>/<c>${{ each }}</c>/<c>${{ insert }}</c> insertion keys with a clear message: they are
    /// template expressions that reshape the document and cannot be honoured locally.
    /// </summary>
    private static void RejectTemplateExpressions(string yamlContent, string displayPath)
    {
        var match = TemplateExpressionKey.Match(yamlContent);
        if (!match.Success)
        {
            return;
        }

        var line = yamlContent.AsSpan(0, match.Index).Count('\n') + 1;
        var kind = match.Groups["kind"].Value;
        var construct = kind switch
        {
            "each" => "'${{ each ... }}' loop insertion",
            "insert" => "'${{ insert }}' insertion",
            _ => "'${{ " + kind + " ... }}' conditional insertion"
        };

        throw new PipelineParseException(
            ErrorCodes.InvalidPipelineStructure,
            $"Azure DevOps template expressions are not supported yet: {construct} at line {line} in {Path.GetFileName(displayPath)}.",
            ErrorContext.FromParserPosition(displayPath, line, match.Groups["indent"].Length + 1),
            new[]
            {
                "Replace the conditional/loop insertion with the concrete YAML it produces for the local run",
                "Template expressions are evaluated by Azure DevOps before the pipeline runs; PDK reads the file as-is"
            });
    }

    /// <summary>
    /// Validates the Azure Pipeline structure and required fields.
    /// </summary>
    /// <param name="pipeline">The Azure Pipeline to validate.</param>
    /// <param name="displayPath">The file path or placeholder for messages.</param>
    /// <exception cref="PipelineParseException">Thrown when validation fails.</exception>
    private static void ValidatePipeline(AzurePipeline pipeline, string displayPath)
    {
        if (pipeline.Extends is not null)
        {
            throw TemplatesNotSupported("extends template", ExtractTemplateName(pipeline.Extends), displayPath);
        }

        // Validate hierarchy pattern - must have exactly one of: stages, jobs, or steps
        if (!pipeline.IsValid())
        {
            throw HierarchyError(displayPath);
        }

        var hierarchyPattern = pipeline.GetHierarchyPattern();

        switch (hierarchyPattern)
        {
            case "multi-stage":
                ValidateStages(pipeline.Stages!, displayPath);
                break;

            case "single-stage":
                ValidateJobs(pipeline.Jobs!, "Pipeline", displayPath);
                break;

            case "simple":
                ValidateSteps(pipeline.Steps!, "Pipeline", displayPath);
                break;

            case "empty":
                throw HierarchyError(displayPath);
        }
    }

    /// <summary>
    /// Validates stages in a multi-stage pipeline.
    /// </summary>
    private static void ValidateStages(List<AzureStage> stages, string displayPath)
    {
        var stageIds = new HashSet<string>();

        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];

            if (stage is null)
            {
                throw StructureError($"Stage entry {i + 1} is empty. Each stage must define a 'stage' identifier and 'jobs'.", displayPath);
            }

            if (stage.Template is not null)
            {
                throw TemplatesNotSupported("stages template", stage.Template, displayPath);
            }

            // Validate stage identifier
            if (string.IsNullOrWhiteSpace(stage.Stage))
            {
                throw StructureError(
                    "Stage is missing required 'stage' identifier.\n" +
                    "Suggestion: Add a unique identifier like: stage: Build",
                    displayPath);
            }

            // Check for duplicate stage IDs
            if (!stageIds.Add(stage.Stage))
            {
                throw StructureError(
                    $"Duplicate stage identifier '{stage.Stage}'. Each stage must have a unique identifier.",
                    displayPath);
            }

            // Validate jobs within the stage
            if (stage.Jobs == null || stage.Jobs.Count == 0)
            {
                throw StructureError($"Stage '{stage.Stage}' must contain at least one job.", displayPath);
            }

            ValidateJobs(stage.Jobs, $"Stage '{stage.Stage}'", displayPath);
        }

        // Validate stage dependencies
        ValidateStageDependencies(stages, displayPath);
    }

    /// <summary>
    /// Validates jobs in a pipeline or stage.
    /// </summary>
    private static void ValidateJobs(List<AzureJob> jobs, string scope, string displayPath)
    {
        var jobIds = new HashSet<string>();

        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];

            if (job is null)
            {
                throw StructureError($"{scope}: job entry {i + 1} is empty. Each job must define a 'job' identifier and 'steps'.", displayPath);
            }

            if (job.Template is not null)
            {
                throw TemplatesNotSupported("jobs template", job.Template, displayPath);
            }

            // Validate job identifier
            var jobId = job.Identifier;
            if (string.IsNullOrWhiteSpace(jobId))
            {
                throw StructureError(
                    "Job is missing required 'job' identifier.\n" +
                    "Suggestion: Add a unique identifier like: job: BuildJob (or deployment: DeployJob)",
                    displayPath);
            }

            // Check for duplicate job IDs (within the same stage/pipeline)
            if (!jobIds.Add(jobId))
            {
                throw StructureError(
                    $"Duplicate job identifier '{jobId}'. Each job must have a unique identifier.",
                    displayPath,
                    jobId);
            }

            // Validate steps
            var steps = job.GetEffectiveSteps();
            if (steps.Count == 0)
            {
                throw StructureError(
                    job.IsDeployment
                        ? $"Deployment job '{jobId}' must contain at least one step under strategy.runOnce.deploy.steps " +
                          "(rolling and canary strategies are read the same way)."
                        : $"Job '{jobId}' must contain at least one step.\n" +
                          "Suggestion: Add at least one step with a script or task.",
                    displayPath,
                    jobId);
            }

            ValidateSteps(steps, $"Job '{jobId}'", displayPath, jobId);
        }

        // Validate job dependencies (within the same scope)
        ValidateJobDependencies(jobs, displayPath);
    }

    /// <summary>
    /// Validates steps in a job or simple pipeline.
    /// </summary>
    private static void ValidateSteps(List<AzureStep> steps, string context, string displayPath, string? jobId = null)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (step is null)
            {
                throw StructureError($"{context}, step {i + 1}: step entry is empty.", displayPath, jobId, $"step {i + 1}");
            }

            if (step.Template is not null)
            {
                throw TemplatesNotSupported("steps template", step.Template, displayPath, jobId);
            }

            var stepType = step.GetStepType();

            // Validate that step has a valid type
            if (stepType == "unknown")
            {
                throw StructureError(
                    $"{context}, step {i + 1}: Step must define one of: task, bash, pwsh, script, powershell, checkout, publish, or download.\n" +
                    "Suggestion: Add a task like: task: DotNetCoreCLI@2 or a script like: bash: echo 'Hello'",
                    displayPath,
                    jobId,
                    $"step {i + 1}");
            }

            // Validate enabled steps have content
            if (step.Enabled != false)
            {
                if (stepType == "task" && string.IsNullOrWhiteSpace(step.Task))
                {
                    throw StructureError($"{context}, step {i + 1}: Task step is missing 'task' field.", displayPath, jobId, $"step {i + 1}");
                }

                if (stepType is "bash" or "pwsh" or "powershell" or "script" && string.IsNullOrWhiteSpace(step.GetScriptContent()))
                {
                    throw StructureError($"{context}, step {i + 1}: Script step is missing content.", displayPath, jobId, $"step {i + 1}");
                }
            }
        }
    }

    /// <summary>
    /// Validates stage dependencies (explicit and implicit ordering) and detects circular dependencies.
    /// </summary>
    private static void ValidateStageDependencies(List<AzureStage> stages, string displayPath)
    {
        var stageGraph = new Dictionary<string, List<string>>();
        var stageSet = new HashSet<string>(stages.Select(s => s.Stage));
        var effectiveDependencies = GetEffectiveStageDependencies(stages);

        // Build dependency graph
        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            var dependencies = effectiveDependencies[i];

            // Validate that all dependencies exist
            foreach (var dep in dependencies)
            {
                if (!stageSet.Contains(dep))
                {
                    throw StructureError(
                        $"Stage '{stage.Stage}' depends on stage '{dep}' which does not exist in the pipeline.\n" +
                        $"Suggestion: Remove the dependency or add the stage '{dep}'.",
                        displayPath);
                }
            }

            stageGraph[stage.Stage] = dependencies;
        }

        // Check for circular dependencies
        var visited = new HashSet<string>();
        var stack = new List<string>();

        foreach (var stageId in stageGraph.Keys)
        {
            var cycle = FindCycle(stageId, stageGraph, visited, stack);
            if (cycle is not null)
            {
                throw PipelineParseException.CircularDependency(displayPath, cycle.Select(id => $"stage '{id}'"));
            }
        }
    }

    /// <summary>
    /// Validates job dependencies within a list of jobs.
    /// </summary>
    private static void ValidateJobDependencies(List<AzureJob> jobs, string displayPath)
    {
        var jobGraph = new Dictionary<string, List<string>>();
        var jobSet = new HashSet<string>(jobs.Select(j => j.Identifier));

        // Build dependency graph
        foreach (var job in jobs)
        {
            var dependencies = job.GetDependencies();

            // Validate that all dependencies exist
            foreach (var dep in dependencies)
            {
                if (!jobSet.Contains(dep))
                {
                    throw StructureError(
                        $"Job '{job.Identifier}' depends on job '{dep}' which does not exist in the same stage/pipeline.\n" +
                        $"Suggestion: Remove the dependency or add the job '{dep}'.",
                        displayPath,
                        job.Identifier);
                }
            }

            jobGraph[job.Identifier] = dependencies;
        }

        // Check for circular dependencies
        var visited = new HashSet<string>();
        var stack = new List<string>();

        foreach (var jobId in jobGraph.Keys)
        {
            var cycle = FindCycle(jobId, jobGraph, visited, stack);
            if (cycle is not null)
            {
                throw PipelineParseException.CircularDependency(displayPath, cycle.Select(id => $"job '{id}'"));
            }
        }
    }

    /// <summary>
    /// Detects circular dependencies using depth-first search; returns the cycle path when one is found.
    /// </summary>
    private static List<string>? FindCycle(
        string nodeId,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        List<string> stack)
    {
        var index = stack.IndexOf(nodeId);
        if (index >= 0)
        {
            var cycle = stack.Skip(index).ToList();
            cycle.Add(nodeId);
            return cycle;
        }

        if (!visited.Add(nodeId))
        {
            return null;
        }

        stack.Add(nodeId);

        if (graph.TryGetValue(nodeId, out var dependencies))
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
    /// Computes the dependencies of each stage: explicit <c>dependsOn</c> when declared (an empty list means
    /// independent), otherwise the previous stage in file order.
    /// </summary>
    private static List<List<string>> GetEffectiveStageDependencies(List<AzureStage> stages)
    {
        var result = new List<List<string>>(stages.Count);

        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];

            if (stage.HasExplicitDependsOn)
            {
                result.Add(stage.GetDependencies());
            }
            else
            {
                result.Add(i == 0 ? new List<string>() : new List<string> { stages[i - 1].Stage });
            }
        }

        return result;
    }

    /// <summary>
    /// Converts an Azure Pipeline to the common PDK Pipeline model.
    /// </summary>
    private Pipeline ConvertToPipeline(AzurePipeline azurePipeline)
    {
        var pipelineVariables = AzureVariableParser.Parse(azurePipeline.Variables, "pipeline", _warnings);

        if (azurePipeline.Resources is not null)
        {
            _warnings.Add("The 'resources' section (repositories, pipelines, containers) is not resolved locally and will be ignored.");
        }

        var pipeline = new Pipeline
        {
            Name = azurePipeline.Name ?? "Azure Pipeline",
            Provider = PipelineProvider.AzureDevOps,
            Variables = pipelineVariables
        };

        var hierarchyPattern = azurePipeline.GetHierarchyPattern();

        switch (hierarchyPattern)
        {
            case "multi-stage":
                ConvertMultiStagePipeline(azurePipeline, pipeline, pipelineVariables);
                break;

            case "single-stage":
                ConvertSingleStagePipeline(azurePipeline, pipeline, pipelineVariables);
                break;

            case "simple":
                ConvertSimplePipeline(azurePipeline, pipeline, pipelineVariables);
                break;
        }

        return pipeline;
    }

    /// <summary>
    /// Converts a multi-stage Azure Pipeline by flattening stages to jobs.
    /// Jobs are named using the pattern: {stageName}_{jobName}
    /// </summary>
    private void ConvertMultiStagePipeline(AzurePipeline azurePipeline, Pipeline pipeline, Dictionary<string, string> pipelineVariables)
    {
        var stages = azurePipeline.Stages!;
        var effectiveDependencies = GetEffectiveStageDependencies(stages);

        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            var stageName = stage.Stage;
            var stageVariables = AzureVariableParser.Parse(stage.Variables, $"stage '{stageName}'", _warnings);

            foreach (var azureJob in stage.Jobs!)
            {
                var jobId = $"{stageName}_{azureJob.Identifier}";

                // Determine pool with inheritance: job → stage → pipeline
                var effectivePool = azureJob.Pool ?? stage.Pool ?? azurePipeline.Pool;

                var job = ConvertToJob(azureJob, effectivePool, pipelineVariables, stageVariables);
                job.Id = jobId;
                job.Stage = stageName;

                // Map stage dependencies (explicit or implicit ordering) to job dependencies:
                // all jobs in this stage depend on all jobs in the stages it depends on
                foreach (var depStage in effectiveDependencies[i])
                {
                    var depStageObj = stages.FirstOrDefault(s => s.Stage == depStage);
                    if (depStageObj?.Jobs != null)
                    {
                        job.DependsOn.AddRange(depStageObj.Jobs.Select(j => $"{depStage}_{j.Identifier}"));
                    }
                }

                // Also add job-level dependencies (within the same stage)
                job.DependsOn.AddRange(azureJob.GetDependencies().Select(dep => $"{stageName}_{dep}"));

                // Transfer stage-level condition to job
                if (!string.IsNullOrWhiteSpace(stage.Condition))
                {
                    if (job.Condition == null)
                    {
                        // Stage has condition, job doesn't - use stage condition
                        job.Condition = new Condition
                        {
                            Expression = stage.Condition,
                            Type = ConditionType.Expression
                        };
                    }
                    else
                    {
                        // Both stage and job have conditions - combine with AND
                        job.Condition.Expression = $"and({stage.Condition}, {job.Condition.Expression})";
                    }
                }

                pipeline.Jobs[jobId] = job;
            }
        }
    }

    /// <summary>
    /// Converts a single-stage Azure Pipeline (jobs without stages).
    /// </summary>
    private void ConvertSingleStagePipeline(AzurePipeline azurePipeline, Pipeline pipeline, Dictionary<string, string> pipelineVariables)
    {
        foreach (var azureJob in azurePipeline.Jobs!)
        {
            var jobId = azureJob.Identifier;

            // Determine pool with inheritance: job → pipeline
            var effectivePool = azureJob.Pool ?? azurePipeline.Pool;

            var job = ConvertToJob(azureJob, effectivePool, pipelineVariables, null);
            job.Id = jobId;

            // Add job-level dependencies
            job.DependsOn.AddRange(azureJob.GetDependencies());

            pipeline.Jobs[jobId] = job;
        }
    }

    /// <summary>
    /// Converts a simple Azure Pipeline (steps without jobs or stages).
    /// Creates a default job named "default".
    /// </summary>
    private void ConvertSimplePipeline(AzurePipeline azurePipeline, Pipeline pipeline, Dictionary<string, string> pipelineVariables)
    {
        var job = new Job
        {
            Id = "default",
            Name = "Default",
            RunsOn = DetermineRunsOn(azurePipeline.Pool),
            Variables = new Dictionary<string, string>(pipelineVariables)
        };

        // Convert steps
        var steps = azurePipeline.Steps!;
        for (var i = 0; i < steps.Count; i++)
        {
            job.Steps.Add(AzureStepMapper.MapStep(steps[i], i, _warnings));
        }

        pipeline.Jobs["default"] = job;
    }

    /// <summary>
    /// Converts an Azure Job (or deployment job) to a common PDK Job model.
    /// </summary>
    private Job ConvertToJob(
        AzureJob azureJob,
        AzurePool? effectivePool,
        Dictionary<string, string> pipelineVariables,
        Dictionary<string, string>? stageVariables)
    {
        var jobId = azureJob.Identifier;
        var jobVariables = AzureVariableParser.Parse(azureJob.Variables, $"job '{jobId}'", _warnings);

        var job = new Job
        {
            Id = jobId,
            Name = azureJob.DisplayName ?? jobId,
            RunsOn = DetermineRunsOn(effectivePool),
            Variables = AzureVariableParser.Merge(pipelineVariables, stageVariables, jobVariables),
            Container = ResolveContainer(azureJob.Container)
        };

        // Convert timeout
        if (azureJob.TimeoutInMinutes.HasValue)
        {
            job.Timeout = TimeSpan.FromMinutes(azureJob.TimeoutInMinutes.Value);
        }

        // Convert condition (raw expression, evaluated at run time)
        if (!string.IsNullOrWhiteSpace(azureJob.Condition))
        {
            job.Condition = new Condition
            {
                Expression = azureJob.Condition,
                Type = ConditionType.Expression
            };
        }

        if (azureJob.IsDeployment && azureJob.Strategy?.GetDeploymentStrategy()?.HasIgnoredHooks == true)
        {
            _warnings.Add($"Deployment job '{jobId}': lifecycle hooks (preDeploy, routeTraffic, postRouteTraffic) are not executed locally; only the 'deploy' steps run.");
        }

        if (azureJob.Services is { Count: > 0 })
        {
            _warnings.Add($"Job '{jobId}': service containers ('services') are not supported locally and will be ignored.");
        }

        // Convert steps
        var steps = azureJob.GetEffectiveSteps();
        for (var i = 0; i < steps.Count; i++)
        {
            job.Steps.Add(AzureStepMapper.MapStep(steps[i], i, _warnings));
        }

        return job;
    }

    /// <summary>
    /// Determines the runner (runs-on value) from the effective pool: <c>vmImage</c> is used as-is (raw, it may be a
    /// <c>$( )</c> macro), a pool <c>name</c> means self-hosted agents, and no pool falls back to ubuntu-latest.
    /// </summary>
    private static string DetermineRunsOn(AzurePool? effectivePool)
    {
        if (effectivePool == null)
        {
            return DefaultRunner;
        }

        // Prefer vmImage for Microsoft-hosted agents
        if (!string.IsNullOrWhiteSpace(effectivePool.VmImage))
        {
            return effectivePool.VmImage.Trim();
        }

        // A named pool means self-hosted agents; the image is chosen by the runner configuration
        if (!string.IsNullOrWhiteSpace(effectivePool.Name))
        {
            return SelfHostedRunner;
        }

        // Fallback to default
        return DefaultRunner;
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

    private static string ExtractTemplateName(object extends)
    {
        var name = extends switch
        {
            string text => text,
            IDictionary<object, object> mapping => YamlValues.AsString(YamlValues.GetValue(mapping, "template")),
            _ => null
        };

        return string.IsNullOrWhiteSpace(name) ? "<unknown>" : name.Trim();
    }

    private static PipelineParseException HierarchyError(string displayPath) => StructureError(
        "Pipeline must define exactly one hierarchy level: stages, jobs, or steps.\n" +
        "Suggestion: Choose one structure:\n" +
        "  - Multi-stage: stages → jobs → steps\n" +
        "  - Single-stage: jobs → steps\n" +
        "  - Simple: steps only",
        displayPath);

    private static PipelineParseException TemplatesNotSupported(string kind, string templateName, string displayPath, string? jobId = null) =>
        StructureError(
            $"Azure DevOps templates are not supported yet: {kind} '{templateName}'.",
            displayPath,
            jobId,
            suggestions: new[]
            {
                "Expand the template inline for the local run",
                "Templates are resolved by Azure DevOps before the pipeline runs; PDK reads the file as-is"
            });

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
}
