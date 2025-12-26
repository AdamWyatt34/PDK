using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Providers.GitHub.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PDK.Providers.GitHub;

/// <summary>
/// Parser for GitHub Actions workflow files.
/// Converts GitHub Actions YAML into the common PDK Pipeline model.
/// </summary>
public class GitHubActionsParser : IPipelineParser
{
    private readonly ILogger<GitHubActionsParser>? _logger;
    private readonly IDeserializer _yamlDeserializer;

    /// <summary>
    /// Initializes a new instance of the GitHubActionsParser.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public GitHubActionsParser(ILogger<GitHubActionsParser>? logger = null)
    {
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Parses a GitHub Actions workflow YAML content into a Pipeline model.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <returns>A Pipeline object representing the workflow.</returns>
    /// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
    public Pipeline Parse(string yamlContent)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new PipelineParseException("YAML content is empty or null");
        }

        _logger?.LogDebug("Starting GitHub Actions workflow parsing");

        GitHubWorkflow workflow;
        try
        {
            workflow = _yamlDeserializer.Deserialize<GitHubWorkflow>(yamlContent);
        }
        catch (YamlException ex)
        {
            _logger?.LogError(ex, "Failed to deserialize YAML content");
            throw new PipelineParseException($"Invalid YAML syntax: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during YAML deserialization");
            throw new PipelineParseException($"Failed to parse YAML: {ex.Message}", ex);
        }

        // Validate the workflow structure
        ValidateWorkflow(workflow);

        // Convert to Pipeline model
        var pipeline = ConvertToPipeline(workflow);

        _logger?.LogInformation("Successfully parsed GitHub Actions workflow: {WorkflowName}", pipeline.Name);

        return pipeline;
    }

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

        return Parse(yamlContent);
    }

    /// <summary>
    /// Determines if this parser can parse the given file.
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
            // Check file path pattern (common GitHub Actions location)
            var isInGitHubWorkflowsFolder = filePath.Contains(".github") &&
                                            filePath.Contains("workflows") &&
                                            (filePath.EndsWith(".yml") || filePath.EndsWith(".yaml"));

            // Quick check: read file and look for GitHub Actions indicators
            var content = File.ReadAllText(filePath);

            // Must have 'jobs' section
            if (!content.Contains("jobs:"))
            {
                return false;
            }

            // Try to deserialize and check for GitHub-specific structure
            var workflow = _yamlDeserializer.Deserialize<GitHubWorkflow>(content);

            // Must have jobs with runs-on (distinctive GitHub Actions feature)
            var hasRunsOn = workflow.Jobs.Values.Any(job => !string.IsNullOrWhiteSpace(job.RunsOn));

            _logger?.LogDebug("CanParse result for {FilePath}: {Result} (GitHub workflows folder: {IsGitHubFolder}, Has runs-on: {HasRunsOn})",
                filePath, hasRunsOn, isInGitHubWorkflowsFolder, hasRunsOn);

            return hasRunsOn;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "CanParse failed for {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Validates the workflow structure and throws if invalid.
    /// </summary>
    private void ValidateWorkflow(GitHubWorkflow workflow)
    {
        // Must have at least one job
        if (workflow.Jobs == null || workflow.Jobs.Count == 0)
        {
            throw new PipelineParseException("Workflow must contain at least one job in the 'jobs' section.");
        }

        // Validate each job
        foreach (var jobEntry in workflow.Jobs)
        {
            var jobId = jobEntry.Key;
            var job = jobEntry.Value;

            // Must have runs-on
            if (string.IsNullOrWhiteSpace(job.RunsOn))
            {
                throw new PipelineParseException($"Job '{jobId}' is missing required 'runs-on' field.");
            }

            // Must have at least one step
            if (job.Steps == null || job.Steps.Count == 0)
            {
                throw new PipelineParseException($"Job '{jobId}' must contain at least one step.");
            }

            // Validate each step
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];

                // Must have either 'uses' or 'run', but not both or neither
                var hasUses = !string.IsNullOrWhiteSpace(step.Uses);
                var hasRun = !string.IsNullOrWhiteSpace(step.Run);

                if (!hasUses && !hasRun)
                {
                    throw new PipelineParseException(
                        $"Job '{jobId}', step {i + 1}: Must specify either 'uses' (action) or 'run' (command).");
                }

                if (hasUses && hasRun)
                {
                    throw new PipelineParseException(
                        $"Job '{jobId}', step {i + 1}: Cannot specify both 'uses' and 'run'. Choose one.");
                }
            }
        }

        // Validate job dependencies (check for circular references)
        ValidateJobDependencies(workflow);
    }

    /// <summary>
    /// Validates job dependencies for circular references.
    /// </summary>
    private void ValidateJobDependencies(GitHubWorkflow workflow)
    {
        var jobGraph = new Dictionary<string, List<string>>();

        // Build dependency graph
        foreach (var jobEntry in workflow.Jobs)
        {
            var jobId = jobEntry.Key;
            var dependencies = ActionMapper.ParseJobDependencies(jobEntry.Value.Needs);

            // Validate that all dependencies exist
            foreach (var dep in dependencies)
            {
                if (!workflow.Jobs.ContainsKey(dep))
                {
                    throw new PipelineParseException(
                        $"Job '{jobId}' depends on job '{dep}' which does not exist in the workflow.");
                }
            }

            jobGraph[jobId] = dependencies;
        }

        // Check for circular dependencies using DFS
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var jobId in jobGraph.Keys)
        {
            if (HasCircularDependency(jobId, jobGraph, visited, recursionStack))
            {
                throw new PipelineParseException(
                    $"Circular dependency detected involving job '{jobId}'. Jobs cannot have circular dependencies.");
            }
        }
    }

    /// <summary>
    /// Detects circular dependencies using depth-first search.
    /// </summary>
    private bool HasCircularDependency(
        string jobId,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack)
    {
        if (recursionStack.Contains(jobId))
        {
            return true; // Circular dependency found
        }

        if (visited.Contains(jobId))
        {
            return false; // Already processed
        }

        visited.Add(jobId);
        recursionStack.Add(jobId);

        if (graph.TryGetValue(jobId, out var dependencies))
        {
            foreach (var dep in dependencies)
            {
                if (HasCircularDependency(dep, graph, visited, recursionStack))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(jobId);
        return false;
    }

    /// <summary>
    /// Converts a GitHubWorkflow to a PDK Pipeline model.
    /// </summary>
    private Pipeline ConvertToPipeline(GitHubWorkflow workflow)
    {
        var pipeline = new Pipeline
        {
            Name = workflow.Name ?? "Unnamed Workflow",
            Provider = PipelineProvider.GitHub,
            Jobs = new Dictionary<string, Job>(),
            Variables = workflow.Env ?? new Dictionary<string, string>()
        };

        // Convert each job
        foreach (var jobEntry in workflow.Jobs)
        {
            var jobId = jobEntry.Key;
            var gitHubJob = jobEntry.Value;

            var job = ConvertToJob(jobId, gitHubJob, workflow.Env);
            pipeline.Jobs[jobId] = job;
        }

        return pipeline;
    }

    /// <summary>
    /// Converts a GitHubJob to a PDK Job model.
    /// </summary>
    private Job ConvertToJob(string jobId, GitHubJob gitHubJob, Dictionary<string, string>? workflowEnv)
    {
        var job = new Job
        {
            Id = jobId,
            Name = gitHubJob.Name ?? jobId,
            RunsOn = gitHubJob.RunsOn,
            Steps = new List<Step>(),
            Environment = ActionMapper.MergeEnvironmentVariables(workflowEnv, gitHubJob.Env, null),
            DependsOn = ActionMapper.ParseJobDependencies(gitHubJob.Needs)
        };

        // Set timeout if specified
        if (gitHubJob.TimeoutMinutes.HasValue)
        {
            job.Timeout = TimeSpan.FromMinutes(gitHubJob.TimeoutMinutes.Value);
        }

        // Set condition if specified
        if (!string.IsNullOrWhiteSpace(gitHubJob.If))
        {
            job.Condition = new Condition
            {
                Expression = gitHubJob.If,
                Type = ConditionType.Expression
            };
        }

        // Convert each step
        for (int i = 0; i < gitHubJob.Steps.Count; i++)
        {
            var gitHubStep = gitHubJob.Steps[i];
            var step = ActionMapper.MapStep(gitHubStep, i);

            // Merge environment variables (workflow -> job -> step)
            step.Environment = ActionMapper.MergeEnvironmentVariables(
                workflowEnv,
                gitHubJob.Env,
                gitHubStep.Env);

            job.Steps.Add(step);
        }

        return job;
    }
}
