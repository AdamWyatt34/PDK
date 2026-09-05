using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Expressions;
using PDK.Core.Models;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.GitLab;

/// <summary>
/// Parser for GitLab CI/CD configuration files (<c>.gitlab-ci.yml</c>).
/// Converts the GitLab job/stage model into the common PDK <see cref="Pipeline"/> model: stages become job
/// dependencies, <c>rules</c>/<c>only</c>/<c>except</c>/<c>when</c> are evaluated at parse time, <c>extends</c>,
/// <c>default</c>, <c>include:local</c>, YAML anchors and <c>!reference</c> tags are resolved, and
/// <c>before_script</c>/<c>script</c>/<c>after_script</c>/<c>artifacts</c> become steps.
/// </summary>
public class GitLabCiParser : IPipelineParser, IPipelineParserWarnings
{
    private const string InlineContentName = ".gitlab-ci.yml";
    private const int MaxIncludeDepth = 100;

    private static readonly Regex RunsOnKey = new(@"^\s*runs-on\s*:", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly string[] AzureOnlyTopLevelKeys = { "pool", "trigger", "pr", "resources", "parameters", "schedules", "steps" };

    private readonly ILogger<GitLabCiParser>? _logger;
    private List<string> _warnings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GitLabCiParser"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public GitLabCiParser(ILogger<GitLabCiParser>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Parses GitLab CI YAML content into a Pipeline model.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <returns>A Pipeline object representing the configuration.</returns>
    /// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
    public Pipeline Parse(string yamlContent) => Parse(yamlContent, PipelineParseOptions.None);

    /// <summary>
    /// Parses GitLab CI YAML content into a Pipeline model, seeding variables and the event from the options.
    /// </summary>
    /// <param name="yamlContent">The YAML content to parse.</param>
    /// <param name="options">Parse options (<c>--param</c>/<c>--var</c> values, workspace, event).</param>
    /// <returns>A Pipeline object representing the configuration.</returns>
    /// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
    public Pipeline Parse(string yamlContent, PipelineParseOptions options) => ParseCore(yamlContent, null, options ?? PipelineParseOptions.None);

    /// <summary>
    /// Parses a GitLab CI file into a Pipeline model.
    /// </summary>
    /// <param name="filePath">Path to the <c>.gitlab-ci.yml</c> file.</param>
    /// <returns>A Pipeline object representing the configuration.</returns>
    /// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
    public Task<Pipeline> ParseFile(string filePath) => ParseFile(filePath, PipelineParseOptions.None);

    /// <summary>
    /// Parses a GitLab CI file into a Pipeline model, seeding variables and the event from the options.
    /// </summary>
    /// <param name="filePath">Path to the <c>.gitlab-ci.yml</c> file.</param>
    /// <param name="options">Parse options (<c>--param</c>/<c>--var</c> values, workspace, event).</param>
    /// <returns>A Pipeline object representing the configuration.</returns>
    /// <exception cref="PipelineParseException">Thrown when parsing fails.</exception>
    public async Task<Pipeline> ParseFile(string filePath, PipelineParseOptions options)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new PipelineParseException("File path is empty or null");
        }

        if (!File.Exists(filePath))
        {
            throw new PipelineParseException($"File not found: {filePath}");
        }

        _logger?.LogDebug("Reading GitLab CI file: {FilePath}", filePath);

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

        return ParseCore(yamlContent, Path.GetFullPath(filePath), options ?? PipelineParseOptions.None);
    }

    /// <summary>
    /// Determines whether this parser can handle the file: a <c>.gitlab-ci.yml</c>/<c>.gitlab-ci.yaml</c> file, or a
    /// <c>.yml</c>/<c>.yaml</c> file whose top level has <c>stages:</c> (a list of names), <c>include:</c> or a job
    /// (a mapping with <c>script:</c>) and that is shaped neither like a GitHub workflow (<c>on:</c> + <c>jobs:</c>,
    /// <c>runs-on</c>) nor like an Azure pipeline (<c>pool:</c>, <c>trigger:</c>, <c>steps:</c>, <c>jobs:</c>/<c>stages:</c> lists of mappings).
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
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension is not (".yml" or ".yaml"))
            {
                return false;
            }

            var fileName = Path.GetFileName(filePath).ToLowerInvariant();
            if (fileName is ".gitlab-ci.yml" or ".gitlab-ci.yaml")
            {
                _logger?.LogDebug("CanParse result for {FilePath}: True (GitLab CI file name)", filePath);
                return true;
            }

            var content = File.ReadAllText(filePath);
            var result = LooksLikeGitLabConfiguration(content);

            _logger?.LogDebug("CanParse result for {FilePath}: {Result}", filePath, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "CanParse failed for {FilePath}", filePath);
            return false;
        }
    }

    private static bool LooksLikeGitLabConfiguration(string content)
    {
        if (RunsOnKey.IsMatch(content))
        {
            return false;
        }

        var stream = new YamlStream();
        using (var reader = new StringReader(content))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[^1].RootNode is not YamlMappingNode root)
        {
            return false;
        }

        var keys = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var (key, value) in root.Children)
        {
            if (key is YamlScalarNode scalar && scalar.Value is not null)
            {
                keys[scalar.Value] = value;
            }
        }

        // GitHub workflow: on + jobs
        if (keys.ContainsKey("jobs") && (keys.ContainsKey("on") || keys.ContainsKey("true")))
        {
            return false;
        }

        // Azure pipeline shapes
        if (AzureOnlyTopLevelKeys.Any(keys.ContainsKey) ||
            keys.TryGetValue("jobs", out var jobs) && jobs is YamlSequenceNode ||
            keys.TryGetValue("stages", out var stages) && stages is YamlSequenceNode stageList && stageList.Children.Count > 0 && stageList.Children[0] is YamlMappingNode ||
            keys.TryGetValue("extends", out var extends) && extends is YamlMappingNode)
        {
            return false;
        }

        if (keys.TryGetValue("stages", out var gitLabStages) && gitLabStages is YamlSequenceNode gitLabStageList &&
            gitLabStageList.Children.All(child => child is YamlScalarNode))
        {
            return true;
        }

        if (keys.ContainsKey("include"))
        {
            return true;
        }

        return keys.Values.OfType<YamlMappingNode>().Any(job =>
            job.Children.Keys.OfType<YamlScalarNode>().Any(k => k.Value is "script" or "trigger"));
    }

    private Pipeline ParseCore(string yamlContent, string? filePath, PipelineParseOptions options)
    {
        _warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new PipelineParseException("YAML content is empty or null");
        }

        var displayPath = filePath ?? InlineContentName;
        var workspace = Path.GetFullPath(string.IsNullOrWhiteSpace(options.WorkspacePath) ? Directory.GetCurrentDirectory() : options.WorkspacePath);

        _logger?.LogDebug("Starting GitLab CI parsing of {DisplayPath}", displayPath);

        var root = GitLabYaml.LoadDocument(yamlContent, displayPath);
        if (root is null || root.Count == 0)
        {
            throw new PipelineParseException(
                ErrorCodes.InvalidPipelineStructure,
                $"{Path.GetFileName(displayPath)} does not define any GitLab CI configuration.",
                new ErrorContext { PipelineFile = displayPath },
                new[] { "Add at least one job with a 'script', e.g. build:\n  script:\n    - echo hello" });
        }

        var context = new GitLabParseContext
        {
            DisplayPath = displayPath,
            Workspace = workspace,
            Options = options,
            Git = GitInfo.Read(workspace),
            Warnings = _warnings,
            Logger = _logger
        };

        var includingDirectory = filePath is not null ? Path.GetDirectoryName(filePath) ?? workspace : workspace;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (filePath is not null)
        {
            visited.Add(filePath);
        }

        root = ProcessIncludes(root, includingDirectory, context, visited, 0);
        GitLabYaml.ResolveReferences(root, displayPath);

        var pipeline = new GitLabPipelineBuilder(root, context).Build();

        foreach (var warning in _warnings)
        {
            _logger?.LogWarning("{ParserWarning}", warning);
        }

        _logger?.LogInformation("Successfully parsed GitLab CI configuration: {PipelineName} ({JobCount} job(s))", pipeline.Name, pipeline.Jobs.Count);

        return pipeline;
    }

    /// <summary>
    /// Merges <c>include:local</c> files below the including document: included files are merged first (in order),
    /// then the including file's keys win. Remote, template, project and component includes are skipped with a warning.
    /// </summary>
    private GitLabMap ProcessIncludes(GitLabMap root, string includingDirectory, GitLabParseContext context, HashSet<string> visited, int depth)
    {
        if (!root.TryGetValue("include", out var includeValue) || includeValue is null)
        {
            root.Remove("include");
            return root;
        }

        root.Remove("include");

        if (depth >= MaxIncludeDepth)
        {
            throw new PipelineParseException(
                ErrorCodes.InvalidPipelineStructure,
                $"'include' nesting is deeper than {MaxIncludeDepth} levels in {Path.GetFileName(context.DisplayPath)}.",
                new ErrorContext { PipelineFile = context.DisplayPath },
                new[] { "Check the included files for an include loop" });
        }

        var items = new List<object?>();
        switch (includeValue)
        {
            case GitLabList list:
                items.AddRange(list);
                break;
            default:
                items.Add(includeValue);
                break;
        }

        object? merged = new GitLabMap();
        foreach (var item in items)
        {
            string? local = null;
            GitLabMap? entry = null;
            switch (item)
            {
                case string path:
                    local = path;
                    break;
                case GitLabMap map:
                    entry = map;
                    local = map["local"] as string;
                    break;
                case null:
                    continue;
                default:
                    context.Warn($"'include' entry of type {GitLabYaml.Describe(item)} is not supported and is ignored.");
                    continue;
            }

            if (entry is not null && entry.ContainsKey("rules") && !IncludeRulesMatch(entry, root, context))
            {
                context.Debug($"Include '{local ?? "(non-local)"}' skipped: its rules did not match.");
                continue;
            }

            if (local is null)
            {
                var kind = entry!.Keys.FirstOrDefault(k => k is "remote" or "template" or "project" or "component") ?? "unknown";
                var target = kind == "unknown" ? string.Join(", ", entry.Keys) : entry[kind] as string ?? kind;
                context.Warn($"'include:{kind}' ({target}) cannot be fetched locally and is ignored; only 'include:local' files are merged.");
                continue;
            }

            var expandedLocal = GitLabVariableExpander.Expand(local, name => RootVariable(root, context, name)).Trim();
            var includePath = ResolveIncludePath(expandedLocal, includingDirectory, context);
            if (!visited.Add(includePath))
            {
                context.Debug($"Include '{expandedLocal}' was already merged; skipping the duplicate.");
                continue;
            }

            string content;
            try
            {
                content = File.ReadAllText(includePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PipelineParseException(
                    ErrorCodes.FileAccessDenied,
                    $"Included file '{expandedLocal}' could not be read: {ex.Message}",
                    new ErrorContext { PipelineFile = context.DisplayPath },
                    new[] { "Check the file permissions of the included file" },
                    ex);
            }

            var included = GitLabYaml.LoadDocument(content, includePath) ?? new GitLabMap();
            included = ProcessIncludes(included, Path.GetDirectoryName(includePath) ?? includingDirectory, context, visited, depth + 1);
            context.Debug($"Merged include '{expandedLocal}' ({included.Count} top-level key(s)).");
            merged = GitLabYaml.DeepMerge(merged, included);
        }

        return (GitLabMap)GitLabYaml.DeepMerge(merged, root)!;
    }

    private static string ResolveIncludePath(string local, string includingDirectory, GitLabParseContext context)
    {
        var relative = local.Replace('\\', '/').TrimStart('/');
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(context.Workspace, relative)),
            Path.GetFullPath(Path.Combine(includingDirectory, relative))
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new PipelineParseException(
            ErrorCodes.FileNotFound,
            $"Included file '{local}' was not found (looked in {string.Join(" and ", candidates.Distinct(StringComparer.OrdinalIgnoreCase))}).",
            new ErrorContext { PipelineFile = context.DisplayPath },
            new[]
            {
                "'include:local' paths are relative to the repository root (the current directory); paths relative to the including file are tried as well",
                "Run pdk from the repository root, or fix the include path"
            });
    }

    private static bool IncludeRulesMatch(GitLabMap entry, GitLabMap root, GitLabParseContext context)
    {
        if (entry["rules"] is not GitLabList rules)
        {
            return true;
        }

        foreach (var item in rules)
        {
            if (item is not GitLabMap rule)
            {
                continue;
            }

            var matched = true;
            if (rule["if"] is string expression)
            {
                try
                {
                    matched = new GitLabRulesEvaluator(name => RootVariable(root, context, name)).Evaluate(expression);
                }
                catch (GitLabExpressionException ex)
                {
                    throw new PipelineParseException(
                        ErrorCodes.InvalidPipelineStructure,
                        $"Invalid 'include:rules' expression '{expression.Trim()}': {ex.Message}",
                        new ErrorContext { PipelineFile = context.DisplayPath },
                        new[] { "Rules compare variables with ==, !=, =~ /regex/ and !~, combined with && and ||" },
                        ex);
                }
            }

            if (matched && rule["exists"] is { } exists)
            {
                matched = GitLabYaml.StringList(exists is GitLabMap m ? m["paths"] : exists)
                    .Select(pattern => GitLabVariableExpander.Expand(pattern, name => RootVariable(root, context, name)))
                    .Any(pattern => File.Exists(Path.Combine(context.Workspace, pattern)) || Directory.Exists(Path.Combine(context.Workspace, pattern)));
            }

            if (matched)
            {
                var when = (rule["when"] as string)?.Trim().ToLowerInvariant();
                return when != "never";
            }
        }

        return false;
    }

    /// <summary>Variables visible to <c>include:rules</c>: CLI values, the root file's global variables and the predefined ones.</summary>
    private static string? RootVariable(GitLabMap root, GitLabParseContext context, string name)
    {
        if (context.Options.Variables.TryGetValue(name, out var cli) || context.Options.Parameters.TryGetValue(name, out cli))
        {
            return cli;
        }

        if (root["variables"] is GitLabMap variables && variables.TryGetValue(name, out var declared))
        {
            return declared switch
            {
                string s => s,
                GitLabMap detail => detail["value"] as string ?? string.Empty,
                _ => null
            };
        }

        var predefined = GitLabPredefinedVariables.Build(new GitLabVariableContext
        {
            Git = context.Git,
            Workspace = context.Workspace,
            EventName = context.Options.EventName
        });

        return predefined.TryGetValue(name, out var value) ? value : null;
    }
}
