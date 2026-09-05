using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps.Models;
using PDK.Providers.AzureDevOps.Templates;
using PDK.Providers.Common;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// Parses Azure DevOps Pipeline YAML files into the common PDK Pipeline model.
/// Supports multi-stage pipelines, single-stage pipelines, and simple pipelines. Templates (<c>template:</c>,
/// <c>extends:</c>), <c>parameters:</c> and <c>${{ }}</c> template expressions are expanded before the document
/// is read (see <see cref="AzureTemplateProcessor"/>); <c>strategy.matrix</c> and <c>strategy.parallel</c> are
/// expanded into one job per leg (see <see cref="AzureMatrixExpander"/>).
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
    /// contains an Azure key (<c>steps</c>, <c>jobs</c>, <c>stages</c>, <c>pool</c>, <c>trigger</c>, <c>pr</c>,
    /// <c>extends</c>, ...) and that is not shaped like a GitHub workflow (<c>on:</c> + <c>jobs:</c>, or <c>runs-on</c>).
    /// The check is structural only, so template expressions and template references do not have to resolve.
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

            // The hierarchy lists must have the Azure shape (a GitHub 'jobs' mapping or a GitLab 'stages' name list fails here)
            var result = LooksLikeAzurePipelineDocument(content);

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
    /// Parses Azure Pipeline YAML content into a common PDK Pipeline model. Parameters resolve to their defaults;
    /// template files are resolved relative to the workspace (current directory).
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <returns>A Pipeline object representing the parsed Azure Pipeline.</returns>
    /// <exception cref="PipelineParseException">Thrown when the YAML content is invalid or cannot be parsed.</exception>
    public Pipeline Parse(string yamlContent) => ParseCore(yamlContent, null, PipelineParseOptions.None);

    /// <summary>
    /// Parses Azure Pipeline YAML content with parse options: <c>--param</c> values for the pipeline's
    /// <c>parameters:</c>, <c>--var</c> values for compile-time <c>${{ variables.x }}</c> lookups, the workspace
    /// template paths resolve against, and the event name behind <c>Build.Reason</c>.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <param name="options">The parse options.</param>
    /// <returns>A Pipeline object representing the parsed Azure Pipeline.</returns>
    /// <exception cref="PipelineParseException">Thrown when the YAML content is invalid or cannot be parsed.</exception>
    public Pipeline Parse(string yamlContent, PipelineParseOptions options) => ParseCore(yamlContent, null, options ?? PipelineParseOptions.None);

    /// <summary>
    /// Parses an Azure Pipeline YAML file into a common PDK Pipeline model.
    /// </summary>
    /// <param name="filePath">The path to the Azure Pipeline YAML file.</param>
    /// <returns>A Task that resolves to a Pipeline object.</returns>
    /// <exception cref="PipelineParseException">Thrown when the file cannot be read or parsed.</exception>
    public Task<Pipeline> ParseFile(string filePath) => ParseFile(filePath, PipelineParseOptions.None);

    /// <summary>
    /// Parses an Azure Pipeline YAML file with parse options (see <see cref="Parse(string, PipelineParseOptions)"/>).
    /// Template files referenced by the pipeline are resolved relative to it.
    /// </summary>
    /// <param name="filePath">The path to the Azure Pipeline YAML file.</param>
    /// <param name="options">The parse options.</param>
    /// <returns>A Task that resolves to a Pipeline object.</returns>
    /// <exception cref="PipelineParseException">Thrown when the file cannot be read or parsed.</exception>
    public async Task<Pipeline> ParseFile(string filePath, PipelineParseOptions options)
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

        return ParseCore(yamlContent, filePath, options ?? PipelineParseOptions.None);
    }

    private static bool LooksLikeGitHubWorkflow(string content) =>
        (GitHubOnKey.IsMatch(content) && GitHubJobsKey.IsMatch(content)) || GitHubRunsOnKey.IsMatch(content);

    /// <summary>
    /// Structural check used by <see cref="CanParse"/>: the document is a mapping whose <c>stages</c>/<c>jobs</c>/<c>steps</c>
    /// values are lists of mappings (job/step/template/<c>${{ if }}</c> entries) or whole-value expressions.
    /// </summary>
    private static bool LooksLikeAzurePipelineDocument(string content)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(content);
        stream.Load(reader);

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return false;
        }

        foreach (var (keyNode, valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode { Value: "stages" or "jobs" or "steps" })
            {
                continue;
            }

            switch (valueNode)
            {
                case YamlSequenceNode sequence:
                    if (sequence.Children.Any(item => item is not YamlMappingNode && !IsExpressionScalar(item)))
                    {
                        // GitLab: 'stages' is a list of names
                        return false;
                    }

                    break;

                case YamlScalarNode scalar when IsExpressionScalar(scalar):
                    // steps: ${{ parameters.steps }}
                    break;

                default:
                    // GitHub: 'jobs' is a mapping
                    return false;
            }
        }

        return true;
    }

    private static bool IsExpressionScalar(YamlNode node) =>
        node is YamlScalarNode scalar && scalar.Value is not null && scalar.Value.Contains("${{", StringComparison.Ordinal);

    private Pipeline ParseCore(string yamlContent, string? filePath, PipelineParseOptions options)
    {
        _warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new PipelineParseException("YAML content cannot be null or empty");
        }

        var displayPath = filePath ?? InlineContentName;

        _logger?.LogDebug("Starting Azure Pipeline parsing");

        // Expand parameters, template expressions, template files and extends before reading the document
        var processor = new AzureTemplateProcessor(options, _warnings);
        var expanded = processor.Process(yamlContent, filePath);
        var nodeParser = expanded.CreateParser();

        AzurePipeline? azurePipeline;
        try
        {
            azurePipeline = _yamlDeserializer.Deserialize<AzurePipeline>(nodeParser);
        }
        catch (YamlException ex)
        {
            _logger?.LogError(ex, "Failed to deserialize YAML content");
            var file = nodeParser.CurrentFile ?? displayPath;
            var content = expanded.Sources.TryGetValue(file, out var source) ? source : yamlContent;
            throw YamlErrorTranslator.Translate(ex, content, file);
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
        var pipeline = ConvertToPipeline(azurePipeline, displayPath);

        foreach (var warning in _warnings)
        {
            _logger?.LogWarning("{ParserWarning}", warning);
        }

        _logger?.LogInformation("Successfully parsed Azure Pipeline: {PipelineName}", pipeline.Name);

        return pipeline;
    }

    /// <summary>
    /// Validates the Azure Pipeline structure and required fields.
    /// </summary>
    /// <param name="pipeline">The Azure Pipeline to validate.</param>
    /// <param name="displayPath">The file path or placeholder for messages.</param>
    /// <exception cref="PipelineParseException">Thrown when validation fails.</exception>
    private static void ValidatePipeline(AzurePipeline pipeline, string displayPath)
    {
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
                throw TemplateNotExpanded("stages", stage.Template, displayPath);
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
                throw TemplateNotExpanded("jobs", job.Template, displayPath);
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
                throw TemplateNotExpanded("steps", step.Template, displayPath, jobId);
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
    private Pipeline ConvertToPipeline(AzurePipeline azurePipeline, string displayPath)
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
                ConvertMultiStagePipeline(azurePipeline, pipeline, pipelineVariables, displayPath);
                break;

            case "single-stage":
                ConvertSingleStagePipeline(azurePipeline, pipeline, pipelineVariables, displayPath);
                break;

            case "simple":
                ConvertSimplePipeline(azurePipeline, pipeline, pipelineVariables);
                break;
        }

        return pipeline;
    }

    /// <summary>
    /// Converts a multi-stage Azure Pipeline by flattening stages to jobs.
    /// Jobs are named using the pattern: {stageName}_{jobName} (matrix legs: {stageName}_{jobName}_{leg}).
    /// </summary>
    private void ConvertMultiStagePipeline(AzurePipeline azurePipeline, Pipeline pipeline, Dictionary<string, string> pipelineVariables, string displayPath)
    {
        var stages = azurePipeline.Stages!;
        var effectiveDependencies = GetEffectiveStageDependencies(stages);

        // Pass 1: convert (and expand) every job so that dependencies can target every expanded instance
        var converted = new List<(AzureStage Stage, int Index, AzureJob AzureJob, List<Job> Jobs)>();
        var idsByStage = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            var stageName = stage.Stage;
            var stageVariables = AzureVariableParser.Parse(stage.Variables, $"stage '{stageName}'", _warnings);
            var stageIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            idsByStage[stageName] = stageIds;

            foreach (var azureJob in stage.Jobs!)
            {
                // Determine pool with inheritance: job → stage → pipeline
                var effectivePool = azureJob.Pool ?? stage.Pool ?? azurePipeline.Pool;

                var jobs = ConvertJobs(azureJob, effectivePool, pipelineVariables, stageVariables);
                foreach (var job in jobs)
                {
                    job.Id = $"{stageName}_{job.Id}";
                    job.Stage = stageName;
                }

                stageIds[azureJob.Identifier] = jobs.Select(job => job.Id).ToList();
                converted.Add((stage, i, azureJob, jobs));
            }
        }

        // Pass 2: dependencies and conditions
        foreach (var (stage, index, azureJob, jobs) in converted)
        {
            var stageName = stage.Stage;

            // Map stage dependencies (explicit or implicit ordering) to job dependencies:
            // all jobs in this stage depend on all jobs in the stages it depends on
            var stageDependencies = effectiveDependencies[index]
                .SelectMany(depStage => idsByStage.TryGetValue(depStage, out var ids) ? ids.Values.SelectMany(list => list) : Enumerable.Empty<string>())
                .ToList();

            // Job-level dependencies (within the same stage) target every leg of a matrix job
            var jobDependencies = azureJob.GetDependencies()
                .SelectMany(dep => idsByStage[stageName].TryGetValue(dep, out var ids) ? ids : new List<string> { $"{stageName}_{dep}" })
                .ToList();

            foreach (var job in jobs)
            {
                job.DependsOn.AddRange(stageDependencies);
                job.DependsOn.AddRange(jobDependencies);

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

                AddJob(pipeline, job, displayPath);
            }
        }
    }

    /// <summary>
    /// Converts a single-stage Azure Pipeline (jobs without stages).
    /// </summary>
    private void ConvertSingleStagePipeline(AzurePipeline azurePipeline, Pipeline pipeline, Dictionary<string, string> pipelineVariables, string displayPath)
    {
        // Pass 1: convert (and expand) every job
        var converted = new List<(AzureJob AzureJob, List<Job> Jobs)>();
        var idsByJob = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var azureJob in azurePipeline.Jobs!)
        {
            // Determine pool with inheritance: job → pipeline
            var effectivePool = azureJob.Pool ?? azurePipeline.Pool;

            var jobs = ConvertJobs(azureJob, effectivePool, pipelineVariables, null);
            idsByJob[azureJob.Identifier] = jobs.Select(job => job.Id).ToList();
            converted.Add((azureJob, jobs));
        }

        // Pass 2: dependencies target every leg of a matrix job
        foreach (var (azureJob, jobs) in converted)
        {
            var dependencies = azureJob.GetDependencies()
                .SelectMany(dep => idsByJob.TryGetValue(dep, out var ids) ? ids : new List<string> { dep })
                .ToList();

            foreach (var job in jobs)
            {
                job.DependsOn.AddRange(dependencies);
                AddJob(pipeline, job, displayPath);
            }
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

    private static void AddJob(Pipeline pipeline, Job job, string displayPath)
    {
        if (pipeline.Jobs.ContainsKey(job.Id))
        {
            throw StructureError(
                $"Duplicate job id '{job.Id}': a matrix or parallel leg produces the same id as another job.\n" +
                "Suggestion: Rename the job or the matrix leg.",
                displayPath,
                job.Id);
        }

        pipeline.Jobs[job.Id] = job;
    }

    /// <summary>
    /// Converts an Azure job into one PDK job, or one per <c>strategy.matrix</c> / <c>strategy.parallel</c> leg.
    /// </summary>
    private List<Job> ConvertJobs(
        AzureJob azureJob,
        AzurePool? effectivePool,
        Dictionary<string, string> pipelineVariables,
        Dictionary<string, string>? stageVariables)
    {
        var legs = azureJob.IsDeployment
            ? Array.Empty<AzureMatrixLeg>()
            : AzureMatrixExpander.Expand(azureJob.Strategy, azureJob.Identifier, _warnings);

        if (legs.Count == 0)
        {
            return new List<Job> { ConvertToJob(azureJob, effectivePool, pipelineVariables, stageVariables) };
        }

        var jobs = new List<Job>(legs.Count);
        foreach (var leg in legs)
        {
            var job = ConvertToJob(azureJob, effectivePool, pipelineVariables, stageVariables);
            AzureMatrixExpander.ApplyLeg(job, leg);
            jobs.Add(job);
        }

        return jobs;
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

    private static PipelineParseException HierarchyError(string displayPath) => StructureError(
        "Pipeline must define exactly one hierarchy level: stages, jobs, or steps.\n" +
        "Suggestion: Choose one structure:\n" +
        "  - Multi-stage: stages → jobs → steps\n" +
        "  - Single-stage: jobs → steps\n" +
        "  - Simple: steps only",
        displayPath);

    /// <summary>
    /// Template references are expanded before deserialization; one that survives sits in a position the expander
    /// does not handle.
    /// </summary>
    private static PipelineParseException TemplateNotExpanded(string listName, string templateName, string displayPath, string? jobId = null) =>
        StructureError(
            $"Template reference '{templateName}' could not be expanded: template references are only supported as items of 'steps', 'jobs', 'stages' and 'variables' lists (found in '{listName}').",
            displayPath,
            jobId,
            suggestions: new[]
            {
                "Move the '- template:' entry directly under steps:, jobs:, stages: or variables:"
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
