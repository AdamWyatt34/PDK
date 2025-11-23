using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// Parses Azure DevOps Pipeline YAML files into the common PDK Pipeline model.
/// Supports multi-stage pipelines, single-stage pipelines, and simple pipelines.
/// </summary>
public class AzureDevOpsParser : IPipelineParser
{
    private readonly ILogger<AzureDevOpsParser>? _logger;
    private readonly IDeserializer _yamlDeserializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureDevOpsParser"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    public AzureDevOpsParser(ILogger<AzureDevOpsParser>? logger = null)
    {
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Determines whether this parser can parse the specified file.
    /// Checks for Azure Pipeline-specific indicators like pool configuration or task definitions.
    /// </summary>
    /// <param name="filePath">The path to the pipeline file.</param>
    /// <returns>True if this parser can handle the file; otherwise, false.</returns>
    public bool CanParse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            // Check file extension
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension != ".yml" && extension != ".yaml")
                return false;

            // Read file content
            var content = File.ReadAllText(filePath);

            // Look for Azure-specific indicators
            // Azure Pipelines typically have: pool, stages, or task definitions
            var hasPool = content.Contains("pool:");
            var hasStages = content.Contains("stages:");
            var hasTask = content.Contains("task:") && content.Contains("@"); // task: Name@version

            // Try to deserialize as Azure Pipeline
            if (hasPool || hasStages || hasTask)
            {
                var pipeline = _yamlDeserializer.Deserialize<AzurePipeline>(content);

                // Check for Azure-specific structure
                // Azure has pool as an object, GitHub has runs-on as a string
                if (pipeline.Pool != null || pipeline.Stages != null)
                    return true;

                // Check if it has jobs with potential pool configuration
                if (pipeline.Jobs != null && pipeline.Jobs.Any(j => j.Pool != null))
                    return true;

                // Check if it has Azure-specific steps (task:, bash:, pwsh:)
                if (pipeline.Steps != null && pipeline.Steps.Any(s =>
                    !string.IsNullOrEmpty(s.Task) ||
                    !string.IsNullOrEmpty(s.Bash) ||
                    !string.IsNullOrEmpty(s.Pwsh)))
                    return true;
            }

            return false;
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
    public Pipeline Parse(string yamlContent)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
            throw new PipelineParseException("YAML content cannot be null or empty.");

        _logger?.LogDebug("Starting Azure Pipeline parsing");

        AzurePipeline azurePipeline;
        try
        {
            azurePipeline = _yamlDeserializer.Deserialize<AzurePipeline>(yamlContent);
        }
        catch (YamlException ex)
        {
            _logger?.LogError(ex, "Failed to deserialize YAML content");
            var lineInfo = ex.Start.Line > 0 ? $" at line {ex.Start.Line}" : "";
            throw new PipelineParseException(
                $"Invalid YAML syntax{lineInfo}: {ex.Message}\n" +
                "Suggestion: Check for proper indentation and YAML formatting.", ex);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during YAML deserialization");
            throw new PipelineParseException($"Failed to parse YAML: {ex.Message}", ex);
        }

        if (azurePipeline == null)
            throw new PipelineParseException("Failed to deserialize YAML content to Azure Pipeline.");

        // Validate the pipeline structure
        ValidatePipeline(azurePipeline);

        // Convert to common Pipeline model
        var pipeline = ConvertToPipeline(azurePipeline);

        _logger?.LogInformation("Successfully parsed Azure Pipeline: {PipelineName}", pipeline.Name);

        return pipeline;
    }

    /// <summary>
    /// Parses an Azure Pipeline YAML file into a common PDK Pipeline model.
    /// </summary>
    /// <param name="filePath">The path to the Azure Pipeline YAML file.</param>
    /// <returns>A Task that resolves to a Pipeline object.</returns>
    /// <exception cref="PipelineParseException">Thrown when the file cannot be read or parsed.</exception>
    public async Task<Pipeline> ParseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new PipelineParseException("File path cannot be null or empty.");

        if (!File.Exists(filePath))
            throw new PipelineParseException($"File not found: {filePath}");

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

        try
        {
            return Parse(yamlContent);
        }
        catch (PipelineParseException ex)
        {
            // Re-throw with file context if not already present
            if (!ex.Message.Contains(filePath))
            {
                throw new PipelineParseException(
                    $"Error parsing file '{filePath}':\n{ex.Message}",
                    ex.InnerException);
            }
            throw;
        }
    }

    /// <summary>
    /// Validates the Azure Pipeline structure and required fields.
    /// </summary>
    /// <param name="pipeline">The Azure Pipeline to validate.</param>
    /// <exception cref="PipelineParseException">Thrown when validation fails.</exception>
    private void ValidatePipeline(AzurePipeline pipeline)
    {
        // Validate hierarchy pattern - must have exactly one of: stages, jobs, or steps
        if (!pipeline.IsValid())
        {
            throw new PipelineParseException(
                "Pipeline must define exactly one hierarchy level: stages, jobs, or steps.\n" +
                "Suggestion: Choose one structure:\n" +
                "  - Multi-stage: stages → jobs → steps\n" +
                "  - Single-stage: jobs → steps\n" +
                "  - Simple: steps only");
        }

        var hierarchyPattern = pipeline.GetHierarchyPattern();

        switch (hierarchyPattern)
        {
            case "multi-stage":
                ValidateStages(pipeline.Stages!);
                break;

            case "single-stage":
                ValidateJobs(pipeline.Jobs!, pipeline.Pool);
                break;

            case "simple":
                ValidateSteps(pipeline.Steps!, "Pipeline");
                break;

            case "empty":
                throw new PipelineParseException(
                    "Pipeline is empty. Must contain at least one of: stages, jobs, or steps.");
        }
    }

    /// <summary>
    /// Validates stages in a multi-stage pipeline.
    /// </summary>
    private void ValidateStages(List<AzureStage> stages)
    {
        var stageIds = new HashSet<string>();

        foreach (var stage in stages)
        {
            // Validate stage identifier
            if (string.IsNullOrWhiteSpace(stage.Stage))
                throw new PipelineParseException(
                    "Stage is missing required 'stage' identifier.\n" +
                    "Suggestion: Add a unique identifier like: stage: Build");

            // Check for duplicate stage IDs
            if (!stageIds.Add(stage.Stage))
                throw new PipelineParseException(
                    $"Duplicate stage identifier '{stage.Stage}'. Each stage must have a unique identifier.");

            // Validate jobs within the stage
            if (stage.Jobs == null || stage.Jobs.Count == 0)
                throw new PipelineParseException(
                    $"Stage '{stage.Stage}' must contain at least one job.");

            ValidateJobs(stage.Jobs, stage.Pool);
        }

        // Validate stage dependencies
        ValidateStageDependencies(stages);
    }

    /// <summary>
    /// Validates jobs in a pipeline or stage.
    /// </summary>
    private void ValidateJobs(List<AzureJob> jobs, AzurePool? inheritedPool)
    {
        var jobIds = new HashSet<string>();

        foreach (var job in jobs)
        {
            // Validate job identifier
            if (string.IsNullOrWhiteSpace(job.Job))
                throw new PipelineParseException(
                    "Job is missing required 'job' identifier.\n" +
                    "Suggestion: Add a unique identifier like: job: BuildJob");

            // Check for duplicate job IDs (within the same stage/pipeline)
            if (!jobIds.Add(job.Job))
                throw new PipelineParseException(
                    $"Duplicate job identifier '{job.Job}'. Each job must have a unique identifier.");

            // Validate steps
            if (job.Steps == null || job.Steps.Count == 0)
                throw new PipelineParseException(
                    $"Job '{job.Job}' must contain at least one step.\n" +
                    "Suggestion: Add at least one step with a script or task.");

            ValidateSteps(job.Steps, $"Job '{job.Job}'");
        }

        // Validate job dependencies (within the same scope)
        ValidateJobDependencies(jobs);
    }

    /// <summary>
    /// Validates steps in a job or simple pipeline.
    /// </summary>
    private void ValidateSteps(List<AzureStep> steps, string context)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var stepType = step.GetStepType();

            // Validate that step has a valid type
            if (stepType == "unknown")
            {
                throw new PipelineParseException(
                    $"{context}, step {i + 1}: Step must define one of: task, bash, pwsh, script, powershell, or checkout.\n" +
                    "Suggestion: Add a task like: task: DotNetCoreCLI@2 or a script like: bash: echo 'Hello'");
            }

            // Validate enabled steps have content
            if (step.Enabled != false)
            {
                if (stepType == "task" && string.IsNullOrEmpty(step.Task))
                    throw new PipelineParseException(
                        $"{context}, step {i + 1}: Task step is missing 'task' field.");

                if (stepType != "task" && stepType != "checkout" && string.IsNullOrEmpty(step.GetScriptContent()))
                    throw new PipelineParseException(
                        $"{context}, step {i + 1}: Script step is missing content.");
            }
        }
    }

    /// <summary>
    /// Validates stage dependencies and detects circular dependencies.
    /// </summary>
    private void ValidateStageDependencies(List<AzureStage> stages)
    {
        var stageGraph = new Dictionary<string, List<string>>();
        var stageSet = new HashSet<string>(stages.Select(s => s.Stage));

        // Build dependency graph
        foreach (var stage in stages)
        {
            var dependencies = stage.GetDependencies();

            // Validate that all dependencies exist
            foreach (var dep in dependencies)
            {
                if (!stageSet.Contains(dep))
                    throw new PipelineParseException(
                        $"Stage '{stage.Stage}' depends on stage '{dep}' which does not exist in the pipeline.\n" +
                        $"Suggestion: Remove the dependency or add the stage '{dep}'.");
            }

            stageGraph[stage.Stage] = dependencies;
        }

        // Check for circular dependencies
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var stageId in stageGraph.Keys)
        {
            if (HasCircularDependency(stageId, stageGraph, visited, recursionStack))
            {
                throw new PipelineParseException(
                    $"Circular dependency detected involving stage '{stageId}'.\n" +
                    "Stages cannot have circular dependencies.");
            }
        }
    }

    /// <summary>
    /// Validates job dependencies within a list of jobs.
    /// </summary>
    private void ValidateJobDependencies(List<AzureJob> jobs)
    {
        var jobGraph = new Dictionary<string, List<string>>();
        var jobSet = new HashSet<string>(jobs.Select(j => j.Job));

        // Build dependency graph
        foreach (var job in jobs)
        {
            var dependencies = job.GetDependencies();

            // Validate that all dependencies exist
            foreach (var dep in dependencies)
            {
                if (!jobSet.Contains(dep))
                    throw new PipelineParseException(
                        $"Job '{job.Job}' depends on job '{dep}' which does not exist in the same stage/pipeline.\n" +
                        $"Suggestion: Remove the dependency or add the job '{dep}'.");
            }

            jobGraph[job.Job] = dependencies;
        }

        // Check for circular dependencies
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var jobId in jobGraph.Keys)
        {
            if (HasCircularDependency(jobId, jobGraph, visited, recursionStack))
            {
                throw new PipelineParseException(
                    $"Circular dependency detected involving job '{jobId}'.\n" +
                    "Jobs cannot have circular dependencies.");
            }
        }
    }

    /// <summary>
    /// Detects circular dependencies using depth-first search.
    /// </summary>
    private bool HasCircularDependency(
        string nodeId,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack)
    {
        if (recursionStack.Contains(nodeId))
            return true;

        if (visited.Contains(nodeId))
            return false;

        visited.Add(nodeId);
        recursionStack.Add(nodeId);

        if (graph.TryGetValue(nodeId, out var dependencies))
        {
            foreach (var dep in dependencies)
            {
                if (HasCircularDependency(dep, graph, visited, recursionStack))
                    return true;
            }
        }

        recursionStack.Remove(nodeId);
        return false;
    }

    /// <summary>
    /// Converts an Azure Pipeline to the common PDK Pipeline model.
    /// </summary>
    private Pipeline ConvertToPipeline(AzurePipeline azurePipeline)
    {
        var pipeline = new Pipeline
        {
            Name = azurePipeline.Name ?? "Azure Pipeline",
            Provider = PipelineProvider.AzureDevOps,
            Variables = azurePipeline.GetVariablesAsDictionary()
        };

        var hierarchyPattern = azurePipeline.GetHierarchyPattern();

        switch (hierarchyPattern)
        {
            case "multi-stage":
                ConvertMultiStagePipeline(azurePipeline, pipeline);
                break;

            case "single-stage":
                ConvertSingleStagePipeline(azurePipeline, pipeline);
                break;

            case "simple":
                ConvertSimplePipeline(azurePipeline, pipeline);
                break;
        }

        return pipeline;
    }

    /// <summary>
    /// Converts a multi-stage Azure Pipeline by flattening stages to jobs.
    /// Jobs are named using the pattern: {stageName}_{jobName}
    /// </summary>
    private void ConvertMultiStagePipeline(AzurePipeline azurePipeline, Pipeline pipeline)
    {
        foreach (var stage in azurePipeline.Stages!)
        {
            var stageName = stage.Stage;
            var stageDisplayName = stage.DisplayName ?? stageName;

            foreach (var azureJob in stage.Jobs)
            {
                var jobId = $"{stageName}_{azureJob.Job}";

                // Determine pool with inheritance: job → stage → pipeline
                var effectivePool = azureJob.Pool ?? stage.Pool ?? azurePipeline.Pool;

                var job = ConvertToJob(azureJob, effectivePool, azurePipeline.Pool);
                job.Id = jobId;

                // Map stage dependencies to job dependencies
                var stageDeps = stage.GetDependencies();
                if (stageDeps.Count > 0)
                {
                    // If this stage depends on other stages, all jobs in this stage
                    // depend on all jobs in the dependent stages
                    var jobDeps = new List<string>();
                    foreach (var depStage in stageDeps)
                    {
                        var depStageObj = azurePipeline.Stages!.FirstOrDefault(s => s.Stage == depStage);
                        if (depStageObj != null)
                        {
                            // Add dependencies to all jobs in the dependent stage
                            jobDeps.AddRange(depStageObj.Jobs.Select(j => $"{depStage}_{j.Job}"));
                        }
                    }
                    job.DependsOn.AddRange(jobDeps);
                }

                // Also add job-level dependencies (within the same stage)
                var jobDeps2 = azureJob.GetDependencies();
                job.DependsOn.AddRange(jobDeps2.Select(dep => $"{stageName}_{dep}"));

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
    private void ConvertSingleStagePipeline(AzurePipeline azurePipeline, Pipeline pipeline)
    {
        foreach (var azureJob in azurePipeline.Jobs!)
        {
            var jobId = azureJob.Job;

            // Determine pool with inheritance: job → pipeline
            var effectivePool = azureJob.Pool ?? azurePipeline.Pool;

            var job = ConvertToJob(azureJob, effectivePool, azurePipeline.Pool);
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
    private void ConvertSimplePipeline(AzurePipeline azurePipeline, Pipeline pipeline)
    {
        var job = new Job
        {
            Id = "default",
            Name = "Default",
            RunsOn = DetermineRunsOn(azurePipeline.Pool, null, null)
        };

        // Convert steps
        for (int i = 0; i < azurePipeline.Steps!.Count; i++)
        {
            var azureStep = azurePipeline.Steps[i];
            var step = AzureStepMapper.MapStep(azureStep, i);

            // Merge environment variables (pipeline → step)
            var pipelineEnv = azurePipeline.GetVariablesAsDictionary();
            if (pipelineEnv.Count > 0 || step.Environment.Count > 0)
            {
                step.Environment = AzureStepMapper.MergeEnvironmentVariables(
                    pipelineEnv,
                    null,
                    step.Environment);
            }

            job.Steps.Add(step);
        }

        pipeline.Jobs["default"] = job;
    }

    /// <summary>
    /// Converts an Azure Job to a common PDK Job model.
    /// </summary>
    private Job ConvertToJob(AzureJob azureJob, AzurePool? effectivePool, AzurePool? pipelinePool)
    {
        var job = new Job
        {
            Id = azureJob.Job,
            Name = azureJob.DisplayName ?? azureJob.Job,
            RunsOn = DetermineRunsOn(effectivePool, null, pipelinePool)
        };

        // Convert timeout
        if (azureJob.TimeoutInMinutes.HasValue)
        {
            job.Timeout = TimeSpan.FromMinutes(azureJob.TimeoutInMinutes.Value);
        }

        // Convert condition
        if (!string.IsNullOrWhiteSpace(azureJob.Condition))
        {
            job.Condition = new Condition
            {
                Expression = azureJob.Condition,
                Type = ConditionType.Expression
            };
        }

        // Convert steps
        for (int i = 0; i < azureJob.Steps.Count; i++)
        {
            var azureStep = azureJob.Steps[i];
            var step = AzureStepMapper.MapStep(azureStep, i);

            // Merge environment variables (job → step)
            // Note: We don't have job-level environment directly, but we can use variables
            if (step.Environment.Count > 0)
            {
                step.Environment = AzureStepMapper.MergeEnvironmentVariables(
                    null,
                    null,
                    step.Environment);
            }

            job.Steps.Add(step);
        }

        return job;
    }

    /// <summary>
    /// Determines the runner (runs-on value) based on pool configuration with inheritance.
    /// Priority: jobPool → stagePool → pipelinePool → default "ubuntu-latest"
    /// </summary>
    private string DetermineRunsOn(AzurePool? jobPool, AzurePool? stagePool, AzurePool? pipelinePool)
    {
        // Use the most specific pool available
        var effectivePool = jobPool ?? stagePool ?? pipelinePool;

        if (effectivePool == null)
            return "ubuntu-latest"; // Default

        // Prefer vmImage for Microsoft-hosted agents
        if (!string.IsNullOrEmpty(effectivePool.VmImage))
            return effectivePool.VmImage;

        // Use pool name for self-hosted agents
        if (!string.IsNullOrEmpty(effectivePool.Name))
            return effectivePool.Name;

        // Fallback to default
        return "ubuntu-latest";
    }
}
