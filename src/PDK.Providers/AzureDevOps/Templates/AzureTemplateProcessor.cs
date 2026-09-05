using System.Text;
using System.Text.RegularExpressions;
using PDK.Core.ErrorHandling;
using PDK.Core.Expressions;
using PDK.Core.Models;
using PDK.Providers.Common;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.AzureDevOps.Templates;

/// <summary>
/// Expands Azure Pipelines templates before the document is deserialized: <c>${{ }}</c> template expressions,
/// <c>${{ if }}</c> / <c>${{ elseif }}</c> / <c>${{ else }}</c> / <c>${{ each }}</c> / <c>${{ insert }}</c>
/// directives, <c>parameters:</c> declarations (with <c>--param</c> overrides), template files referenced from
/// <c>steps</c>, <c>jobs</c>, <c>stages</c> and <c>variables</c> lists, and <c>extends:</c>.
/// </summary>
/// <remarks>
/// The processor works on the YamlDotNet node graph and produces a new graph; every produced node is mapped back
/// to the file and position it came from so that later errors still point at the right line. Expressions are
/// evaluated with the shared expression engine against the <c>parameters</c> and <c>variables</c> contexts (and
/// the loop variables of enclosing <c>${{ each }}</c> directives). <c>$( )</c> macros and <c>$[ ]</c> runtime
/// expressions are left untouched.
/// </remarks>
public sealed partial class AzureTemplateProcessor
{
    /// <summary>The maximum nesting depth of template files.</summary>
    public const int MaxIncludeDepth = 20;

    /// <summary>The maximum number of nodes the expanded document may contain.</summary>
    public const int MaxNodes = 250_000;

    private const string InlineContentName = "pipeline";
    private const string ExpressionStart = "${{";
    private const string ExpressionEnd = "}}";

    private static readonly HashSet<string> TemplateContainers = new(StringComparer.Ordinal) { "steps", "jobs", "stages", "variables" };
    private static readonly HashSet<string> ScopeOwnerContainers = new(StringComparer.Ordinal) { "stages", "jobs" };
    private static readonly HashSet<string> RootSkipKeys = new(StringComparer.Ordinal) { "parameters" };
    private static readonly HashSet<string> ExtendsRootSkipKeys = new(StringComparer.Ordinal) { "parameters", "extends" };
    private static readonly HashSet<string> RuntimeOnlyFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "success", "succeeded", "failure", "failed", "always", "cancelled", "canceled", "succeededOrFailed", "hashFiles"
    };

    private static readonly Regex DirectivePattern = new(
        @"^(?<kind>if|elseif|else|each|insert)(?=[\s(]|$)(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex EachPattern = new(
        @"^(?<var>[A-Za-z_][A-Za-z0-9_]*)\s+in\s+(?<expr>.+)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly string[] ExpressionSuggestions =
    {
        "Template expressions are evaluated when the pipeline is loaded; they can use parameters.*, variables.* and ${{ each }} loop variables",
        "Available functions: eq, ne, and, or, not, xor, lt, le, gt, ge, in, notIn, contains, containsValue, startsWith, endsWith, coalesce, format, join, length, lower, upper, trim, replace, split, convertToJson, counter",
        "Runtime values (dependencies, step outputs, $(macros)) are not available here; use $[ ] runtime expressions or condition: for those"
    };

    private readonly PipelineParseOptions _options;
    private readonly ICollection<string> _warnings;
    private readonly Func<string, string?> _readFile;

    private Dictionary<YamlNode, YamlNodeOrigin> _origins = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, string> _sources = new(StringComparer.Ordinal);
    private Dictionary<string, YamlMappingNode> _documents = new(StringComparer.Ordinal);
    private List<IncludeFrame> _includeChain = new();
    private Lazy<IReadOnlyDictionary<string, string>> _predefined = new(() => new Dictionary<string, string>());
    private string _rootDirectory = string.Empty;
    private string _workspace = string.Empty;
    private int _nodeCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureTemplateProcessor"/> class.
    /// </summary>
    /// <param name="options">Parse options: <c>--param</c> values, <c>--var</c> values, workspace and event.</param>
    /// <param name="warnings">Sink for non-fatal findings (ignored <c>--param</c> names, ...).</param>
    /// <param name="fileReader">
    /// Reads a template file by full path, returning null when it does not exist. Defaults to the file system.
    /// </param>
    public AzureTemplateProcessor(PipelineParseOptions? options, ICollection<string>? warnings = null, Func<string, string?>? fileReader = null)
    {
        _options = options ?? PipelineParseOptions.None;
        _warnings = warnings ?? new List<string>();
        _readFile = fileReader ?? ReadFileFromDisk;
    }

    /// <summary>
    /// Expands <paramref name="yamlContent"/>. Template paths are resolved relative to <paramref name="filePath"/>
    /// (or the workspace / current directory for inline content).
    /// </summary>
    /// <param name="yamlContent">The pipeline YAML.</param>
    /// <param name="filePath">The pipeline file path, or null for inline content.</param>
    /// <returns>The expanded document.</returns>
    /// <exception cref="PipelineParseException">Thrown when a template, expression or parameter is invalid.</exception>
    public AzureTemplateResult Process(string yamlContent, string? filePath)
    {
        ArgumentNullException.ThrowIfNull(yamlContent);

        _origins = new Dictionary<YamlNode, YamlNodeOrigin>(ReferenceEqualityComparer.Instance);
        _sources = new Dictionary<string, string>(StringComparer.Ordinal);
        _documents = new Dictionary<string, YamlMappingNode>(StringComparer.Ordinal);
        _includeChain = new List<IncludeFrame>();
        _nodeCount = 0;

        string displayPath;
        string? fullPath = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            displayPath = InlineContentName;
            _rootDirectory = _options.WorkspacePath ?? Directory.GetCurrentDirectory();
        }
        else
        {
            displayPath = filePath;
            fullPath = Path.GetFullPath(filePath);
            _rootDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        }

        _workspace = _options.WorkspacePath ?? _rootDirectory;

        var root = LoadDocument(yamlContent, displayPath);
        _includeChain.Add(new IncludeFrame(fullPath ?? displayPath, null, 0));

        var pipelineName = ReadLiteralName(root) ?? "Azure Pipeline";
        _predefined = new Lazy<IReadOnlyDictionary<string, string>>(() => BuildPredefinedVariables(pipelineName));
        var variables = new CompileTimeVariables(_predefined, _options.Variables);

        var declarations = ReadParameterDeclarations(root, displayPath);
        var parameters = BindRootParameters(declarations, displayPath);
        var scope = new AzureTemplateScope(displayPath, parameters, variables, null);

        var expanded = TryGetEntry(root, "extends", out var extends)
            ? ExpandExtends(root, extends, scope, displayPath)
            : ExpandMapping(root, scope, displayPath, null, RootSkipKeys, null);

        return new AzureTemplateResult(expanded, _origins, _sources, displayPath);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Document loading
    // ---------------------------------------------------------------------------------------------------------

    private static string? ReadFileFromDisk(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>Loads a YAML document; an empty document yields an empty mapping.</summary>
    private YamlMappingNode LoadDocument(string content, string file)
    {
        _sources[file] = content;

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(content);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            throw YamlErrorTranslator.Translate(ex, content, file);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is null)
        {
            return new YamlMappingNode();
        }

        var rootNode = stream.Documents[0].RootNode;
        if (rootNode is YamlMappingNode mapping)
        {
            return mapping;
        }

        if (rootNode is YamlScalarNode scalar && AzureTemplateValues.IsNullLiteral(scalar.Value))
        {
            return new YamlMappingNode();
        }

        throw new PipelineParseException(
            ErrorCodes.InvalidPipelineStructure,
            $"Invalid YAML structure in {Path.GetFileName(file)} at line {rootNode.Start.Line}, column {rootNode.Start.Column}: " +
            $"the top level must be a mapping of pipeline keys (stages, jobs, steps, pool, ...), not {DescribeNode(rootNode)}.",
            ErrorContext.FromParserPosition(file, rootNode.Start.Line, rootNode.Start.Column),
            new[] { "Start the file with pipeline keys such as 'trigger:', 'pool:' and 'steps:' / 'jobs:' / 'stages:'" });
    }

    private static string? ReadLiteralName(YamlMappingNode root)
    {
        if (TryGetEntry(root, "name", out var node) && node is YamlScalarNode scalar &&
            !string.IsNullOrWhiteSpace(scalar.Value) && !scalar.Value.Contains(ExpressionStart, StringComparison.Ordinal))
        {
            return scalar.Value;
        }

        return null;
    }

    private IReadOnlyDictionary<string, string> BuildPredefinedVariables(string pipelineName)
    {
        var info = new JobRuntimeInfo
        {
            Workspace = _workspace,
            Provider = PipelineProvider.AzureDevOps,
            PipelineName = pipelineName,
            EventName = _options.EventName,
            Git = GitInfo.Read(_workspace)
        };

        return PipelineContextBuilder.AzurePredefinedVariables(info, null);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Expansion
    // ---------------------------------------------------------------------------------------------------------

    private YamlNode ExpandNode(YamlNode node, AzureTemplateScope scope, string file, string? containerKey)
    {
        return node switch
        {
            YamlScalarNode scalar => ExpandScalar(scalar, scope, file),
            YamlMappingNode mapping => ExpandMapping(mapping, scope, file, containerKey, null, null),
            YamlSequenceNode sequence => ExpandSequence(sequence, scope, file, containerKey, null),
            _ => throw TemplateError(file, node, "Unsupported YAML node (aliases must resolve to a scalar, mapping or list).")
        };
    }

    /// <summary>
    /// Expands a mapping: directive keys are evaluated and their content merged in place, other keys and values
    /// are expanded. A <c>variables</c> entry of the root, a stage or a job registers its variables in
    /// <paramref name="scope"/> so that later entries can reference them.
    /// </summary>
    private YamlMappingNode ExpandMapping(
        YamlMappingNode source,
        AzureTemplateScope scope,
        string file,
        string? parentKey,
        ISet<string>? skipKeys,
        Action<string, YamlNode>? entryProduced)
    {
        var result = NewMapping(source, file);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var chain = new BranchChain();
        var ownsVariables = parentKey is null || ScopeOwnerContainers.Contains(parentKey);

        foreach (var (keyNode, valueNode) in source.Children)
        {
            var keyText = KeyText(keyNode, file);
            if (skipKeys is not null && skipKeys.Contains(keyText))
            {
                continue;
            }

            var directive = TryParseDirective(keyText);
            if (directive is not null)
            {
                ValidateDirective(directive, keyNode, file);

                switch (directive.Kind)
                {
                    case DirectiveKind.If:
                    case DirectiveKind.ElseIf:
                    case DirectiveKind.Else:
                        if (EvaluateBranch(directive, chain, keyNode, scope, file))
                        {
                            Merge(result, keys, ExpandDirectiveValueAsMapping(directive, valueNode, scope, file, parentKey), file, entryProduced);
                        }

                        continue;

                    case DirectiveKind.Each:
                        chain.Reset();
                        foreach (var item in EnumerateEach(directive, keyNode, scope, file))
                        {
                            var loopScope = scope.WithLoopVariable(directive.Variable!, item);
                            Merge(result, keys, ExpandDirectiveValueAsMapping(directive, valueNode, loopScope, file, parentKey), file, entryProduced);
                        }

                        continue;

                    case DirectiveKind.Insert:
                        chain.Reset();
                        Merge(result, keys, ExpandDirectiveValueAsMapping(directive, valueNode, scope, file, parentKey), file, entryProduced);
                        continue;
                }
            }

            chain.Reset();

            var expandedKey = ExpandKey(keyNode, scope, file);
            var expandedKeyText = expandedKey.Value ?? string.Empty;
            var expandedValue = ownsVariables && expandedKeyText == "variables"
                ? ExpandVariablesSection(valueNode, scope, file)
                : ExpandNode(valueNode, scope, file, expandedKeyText);

            AddEntry(result, keys, expandedKey, expandedValue, file, entryProduced);
        }

        return result;
    }

    /// <summary>
    /// Expands a sequence: directive items are evaluated and their items spliced in place, template references
    /// (<c>- template:</c>) are included, and a whole-expression item that produces a list is flattened into the
    /// sequence (<c>- ${{ parameters.steps }}</c>).
    /// </summary>
    private YamlSequenceNode ExpandSequence(
        YamlSequenceNode source,
        AzureTemplateScope scope,
        string file,
        string? containerKey,
        Action<YamlNode>? produced)
    {
        var result = NewSequence(source, file);
        var chain = new BranchChain();
        var itemsOwnScope = containerKey is not null && ScopeOwnerContainers.Contains(containerKey);

        void Emit(YamlNode node)
        {
            result.Add(node);
            produced?.Invoke(node);
        }

        foreach (var item in source.Children)
        {
            if (item is YamlMappingNode directiveMapping && TryGetDirectives(directiveMapping, file, out var directives))
            {
                foreach (var (directive, keyNode, valueNode) in directives)
                {
                    ValidateDirective(directive, keyNode, file);

                    switch (directive.Kind)
                    {
                        case DirectiveKind.If:
                        case DirectiveKind.ElseIf:
                        case DirectiveKind.Else:
                            if (EvaluateBranch(directive, chain, keyNode, scope, file))
                            {
                                foreach (var node in ExpandDirectiveValueAsItems(directive, valueNode, scope, file, containerKey))
                                {
                                    Emit(node);
                                }
                            }

                            break;

                        case DirectiveKind.Each:
                            chain.Reset();
                            foreach (var element in EnumerateEach(directive, keyNode, scope, file))
                            {
                                var loopScope = scope.WithLoopVariable(directive.Variable!, element);
                                foreach (var node in ExpandDirectiveValueAsItems(directive, valueNode, loopScope, file, containerKey))
                                {
                                    Emit(node);
                                }
                            }

                            break;

                        case DirectiveKind.Insert:
                            throw TemplateError(
                                file,
                                keyNode,
                                $"'{directive.Text}' can only be used inside a mapping; to insert list items use '- ${{{{ if ... }}}}:' or a whole-value expression such as '- ${{{{ parameters.steps }}}}'.");
                    }
                }

                continue;
            }

            chain.Reset();

            if (item is YamlMappingNode reference && containerKey is not null && TemplateContainers.Contains(containerKey) &&
                TryGetEntry(reference, "template", out _))
            {
                foreach (var node in IncludeTemplate(reference, scope, file, containerKey))
                {
                    Emit(node);
                }

                continue;
            }

            if (item is YamlScalarNode scalar && IsWholeExpression(scalar.Value ?? string.Empty, out var expression))
            {
                var value = Evaluate(expression, scope, scalar, file);
                switch (value)
                {
                    case IReadOnlyList<object?> list:
                        // A list-valued expression used as a list item is flattened into the list (- ${{ parameters.steps }})
                        foreach (var element in list)
                        {
                            Emit(ValueToNode(element, scalar, file));
                        }

                        break;

                    case IReadOnlyDictionary<string, object?>:
                        Emit(ValueToNode(value, scalar, file));
                        break;

                    default:
                        Emit(ScalarFromValue(value, expression, scalar, file));
                        break;
                }

                continue;
            }

            var itemScope = itemsOwnScope && item is YamlMappingNode ? scope.CreateChild() : scope;
            Emit(ExpandNode(item, itemScope, file, containerKey));
        }

        return result;
    }

    /// <summary>
    /// Expands a <c>variables</c> block (mapping, list or expression) and registers every produced variable in
    /// <paramref name="scope"/> as soon as it is produced, so later entries can reference earlier ones.
    /// </summary>
    private YamlNode ExpandVariablesSection(YamlNode valueNode, AzureTemplateScope scope, string file)
    {
        switch (valueNode)
        {
            case YamlSequenceNode sequence:
                return ExpandSequence(sequence, scope, file, "variables", item => RegisterVariableItem(item, scope));

            case YamlMappingNode mapping:
                return ExpandMapping(mapping, scope, file, "variables", null, (name, value) => RegisterVariable(name, value, scope));

            default:
            {
                var expanded = ExpandNode(valueNode, scope, file, "variables");
                RegisterVariablesFromNode(expanded, scope);
                return expanded;
            }
        }
    }

    private static void RegisterVariablesFromNode(YamlNode node, AzureTemplateScope scope)
    {
        switch (node)
        {
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                {
                    RegisterVariableItem(item, scope);
                }

                break;

            case YamlMappingNode mapping:
                foreach (var (key, value) in mapping.Children)
                {
                    RegisterVariable(AzureTemplateValues.KeyText(key), value, scope);
                }

                break;
        }
    }

    private static void RegisterVariableItem(YamlNode item, AzureTemplateScope scope)
    {
        if (item is not YamlMappingNode mapping || !TryGetEntry(mapping, "name", out var nameNode) || nameNode is not YamlScalarNode name)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(name.Value))
        {
            RegisterVariable(name.Value, TryGetEntry(mapping, "value", out var value) ? value : null, scope);
        }
    }

    private static void RegisterVariable(string name, YamlNode? value, AzureTemplateScope scope)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // Variables are strings at run time; keep the same shape at compile time
        var text = value switch
        {
            null => string.Empty,
            YamlScalarNode scalar => scalar.Value ?? string.Empty,
            _ => YamlValues.AsString(AzureTemplateValues.ToValue(value)) ?? string.Empty
        };

        scope.Variables.Set(name, text);
    }

    private YamlScalarNode ExpandKey(YamlNode keyNode, AzureTemplateScope scope, string file)
    {
        if (keyNode is not YamlScalarNode scalar)
        {
            throw TemplateError(file, keyNode, "Mapping keys must be scalars.");
        }

        var expanded = ExpandScalar(scalar, scope, file);
        if (expanded is YamlScalarNode expandedScalar)
        {
            return expandedScalar;
        }

        throw TemplateError(file, keyNode, $"The template expression in key '{scalar.Value}' must produce a string, not {DescribeNode(expanded)}.");
    }

    /// <summary>
    /// Expands a scalar: a scalar that is exactly one expression is replaced structurally when the expression
    /// produces an object or a list; any other expression is rendered as text in place.
    /// </summary>
    private YamlNode ExpandScalar(YamlScalarNode scalar, AzureTemplateScope scope, string file)
    {
        var text = scalar.Value ?? string.Empty;
        if (!text.Contains(ExpressionStart, StringComparison.Ordinal))
        {
            Reuse(scalar, file);
            return scalar;
        }

        if (IsWholeExpression(text, out var expression))
        {
            var value = Evaluate(expression, scope, scalar, file);
            return value is IReadOnlyDictionary<string, object?> or IReadOnlyList<object?>
                ? ValueToNode(value, scalar, file)
                : ScalarFromValue(value, expression, scalar, file);
        }

        var interpolated = Interpolate(text, scope, scalar, file);
        return NewScalar(interpolated, StyleFor(scalar, interpolated), scalar, file);
    }

    private YamlScalarNode ScalarFromValue(object? value, string expression, YamlScalarNode source, string file)
    {
        if (!AzureTemplateValues.TryToText(value, out var text))
        {
            throw TemplateError(
                file,
                source,
                $"Template expression '${{{{ {expression.Trim()} }}}}' produced {AzureTemplateValues.DescribeType(value)} where a scalar value is required.",
                new[] { "Use the expression as the whole value of a key or list item to insert a mapping or list", "Use convertToJson(...) to insert it as text" });
        }

        return NewScalar(text, StyleFor(source, text), source, file);
    }

    private string Interpolate(string text, AzureTemplateScope scope, YamlScalarNode source, string file)
    {
        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (true)
        {
            var start = text.IndexOf(ExpressionStart, index, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, start - index);

            var close = text.IndexOf(ExpressionEnd, start + ExpressionStart.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                throw TemplateError(file, source, $"Unterminated template expression in '{Truncate(text)}': missing '}}}}'.");
            }

            var expression = text[(start + ExpressionStart.Length)..close];
            var value = Evaluate(expression, scope, source, file);
            if (!AzureTemplateValues.TryToText(value, out var rendered))
            {
                throw TemplateError(
                    file,
                    source,
                    $"Template expression '${{{{ {expression.Trim()} }}}}' produced {AzureTemplateValues.DescribeType(value)}, which cannot be inserted into text.",
                    new[] { "Use convertToJson(...) or join(...) to render it as text", "Or use the expression as the whole value to insert it structurally" });
            }

            builder.Append(rendered);
            index = close + ExpressionEnd.Length;
        }

        return builder.ToString();
    }

    private static bool IsWholeExpression(string text, out string expression)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith(ExpressionStart, StringComparison.Ordinal) &&
            trimmed.EndsWith(ExpressionEnd, StringComparison.Ordinal) &&
            trimmed.Length >= ExpressionStart.Length + ExpressionEnd.Length &&
            trimmed.IndexOf(ExpressionEnd, ExpressionStart.Length, StringComparison.Ordinal) == trimmed.Length - ExpressionEnd.Length)
        {
            expression = trimmed[ExpressionStart.Length..^ExpressionEnd.Length];
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static ScalarStyle StyleFor(YamlScalarNode source, string text)
    {
        if (NeedsQuoting(text))
        {
            return ScalarStyle.DoubleQuoted;
        }

        return source.Style == ScalarStyle.Any ? ScalarStyle.Plain : source.Style;
    }

    /// <summary>A plain scalar with this text would be read as null; quote it so an empty string stays a string.</summary>
    private static bool NeedsQuoting(string text) => AzureTemplateValues.IsNullLiteral(text);

    // ---------------------------------------------------------------------------------------------------------
    // Directives
    // ---------------------------------------------------------------------------------------------------------

    private enum DirectiveKind
    {
        If,
        ElseIf,
        Else,
        Each,
        Insert
    }

    private sealed record Directive(DirectiveKind Kind, string? Expression, string? Variable, string Text, string? Problem);

    private sealed class BranchChain
    {
        public bool Active { get; set; }

        public bool Taken { get; set; }

        public void Reset() => Active = false;
    }

    private sealed record IncludeFrame(string File, string? ReferencedFrom, int ReferenceLine);

    private static Directive? TryParseDirective(string keyText)
    {
        var trimmed = keyText.Trim();
        if (!trimmed.StartsWith(ExpressionStart, StringComparison.Ordinal) ||
            !trimmed.EndsWith(ExpressionEnd, StringComparison.Ordinal) ||
            trimmed.Length < ExpressionStart.Length + ExpressionEnd.Length ||
            trimmed.IndexOf(ExpressionEnd, ExpressionStart.Length, StringComparison.Ordinal) != trimmed.Length - ExpressionEnd.Length)
        {
            return null;
        }

        var inner = trimmed[ExpressionStart.Length..^ExpressionEnd.Length].Trim();
        var match = DirectivePattern.Match(inner);
        if (!match.Success)
        {
            return null;
        }

        var rest = match.Groups["rest"].Value.Trim();
        switch (match.Groups["kind"].Value)
        {
            case "if":
                return new Directive(DirectiveKind.If, rest.Length > 0 ? rest : null, null, trimmed, rest.Length > 0 ? null : "'${{ if }}' needs a condition, e.g. ${{ if eq(parameters.deploy, true) }}");

            case "elseif":
                return new Directive(DirectiveKind.ElseIf, rest.Length > 0 ? rest : null, null, trimmed, rest.Length > 0 ? null : "'${{ elseif }}' needs a condition");

            case "else":
                return new Directive(DirectiveKind.Else, null, null, trimmed, rest.Length == 0 ? null : "'${{ else }}' does not take a condition");

            case "each":
            {
                var each = EachPattern.Match(rest);
                return each.Success
                    ? new Directive(DirectiveKind.Each, each.Groups["expr"].Value.Trim(), each.Groups["var"].Value, trimmed, null)
                    : new Directive(DirectiveKind.Each, null, null, trimmed, "'${{ each }}' must be written as ${{ each item in parameters.items }}");
            }

            default:
                return new Directive(DirectiveKind.Insert, null, null, trimmed, rest.Length == 0 ? null : "'${{ insert }}' does not take arguments");
        }
    }

    private void ValidateDirective(Directive directive, YamlNode keyNode, string file)
    {
        if (directive.Problem is not null)
        {
            throw TemplateError(file, keyNode, $"Invalid directive '{directive.Text}': {directive.Problem}.");
        }
    }

    /// <summary>Whether every key of <paramref name="mapping"/> is a directive (a list item such as <c>- ${{ if }}:</c>).</summary>
    private bool TryGetDirectives(YamlMappingNode mapping, string file, out List<(Directive Directive, YamlNode Key, YamlNode Value)> directives)
    {
        directives = new List<(Directive, YamlNode, YamlNode)>();
        if (mapping.Children.Count == 0)
        {
            return false;
        }

        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            var directive = TryParseDirective(KeyText(keyNode, file));
            if (directive is null)
            {
                return false;
            }

            directives.Add((directive, keyNode, valueNode));
        }

        return true;
    }

    private bool EvaluateBranch(Directive directive, BranchChain chain, YamlNode keyNode, AzureTemplateScope scope, string file)
    {
        switch (directive.Kind)
        {
            case DirectiveKind.If:
                chain.Active = true;
                chain.Taken = ExpressionValue.IsTruthy(Evaluate(directive.Expression!, scope, keyNode, file));
                return chain.Taken;

            case DirectiveKind.ElseIf:
                if (!chain.Active)
                {
                    throw TemplateError(file, keyNode, $"'{directive.Text}' must directly follow a '${{{{ if }}}}' or '${{{{ elseif }}}}' entry.");
                }

                if (chain.Taken)
                {
                    return false;
                }

                chain.Taken = ExpressionValue.IsTruthy(Evaluate(directive.Expression!, scope, keyNode, file));
                return chain.Taken;

            default:
                if (!chain.Active)
                {
                    throw TemplateError(file, keyNode, $"'{directive.Text}' must directly follow a '${{{{ if }}}}' or '${{{{ elseif }}}}' entry.");
                }

                chain.Active = false;
                return !chain.Taken;
        }
    }

    private IEnumerable<object?> EnumerateEach(Directive directive, YamlNode keyNode, AzureTemplateScope scope, string file)
    {
        var value = Evaluate(directive.Expression!, scope, keyNode, file);
        switch (value)
        {
            case null:
                return Array.Empty<object?>();

            case string text when text.Length == 0:
                return Array.Empty<object?>();

            case IReadOnlyList<object?> list:
                return list;

            case IReadOnlyDictionary<string, object?> mapping:
                return mapping.Select(pair =>
                {
                    var entry = ExpressionValue.NewObject();
                    entry["key"] = pair.Key;
                    entry["value"] = pair.Value;
                    return (object?)entry;
                }).ToList();

            default:
                throw TemplateError(
                    file,
                    keyNode,
                    $"'{directive.Text}' cannot iterate over {AzureTemplateValues.DescribeType(value)}; the expression must produce a list or a mapping.",
                    new[] { "Declare the parameter with 'type: object' (or a list type such as stepList) and give it a list or mapping value" });
        }
    }

    private YamlMappingNode ExpandDirectiveValueAsMapping(Directive directive, YamlNode valueNode, AzureTemplateScope scope, string file, string? parentKey)
    {
        switch (valueNode)
        {
            case YamlMappingNode mapping:
                return ExpandMapping(mapping, scope, file, parentKey, null, null);

            case YamlScalarNode scalar:
            {
                var text = scalar.Value ?? string.Empty;
                if (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && AzureTemplateValues.IsNullLiteral(text))
                {
                    return NewMapping(scalar, file);
                }

                if (IsWholeExpression(text, out var expression))
                {
                    var value = Evaluate(expression, scope, scalar, file);
                    if (value is null)
                    {
                        return NewMapping(scalar, file);
                    }

                    if (value is IReadOnlyDictionary<string, object?>)
                    {
                        return (YamlMappingNode)ValueToNode(value, scalar, file);
                    }

                    throw TemplateError(file, scalar, $"The value of '{directive.Text}' must be a mapping, but the expression produced {AzureTemplateValues.DescribeType(value)}.");
                }

                throw TemplateError(file, scalar, $"The value of '{directive.Text}' inside a mapping must be a mapping of keys to insert, not a scalar.");
            }

            default:
                throw TemplateError(
                    file,
                    valueNode,
                    $"The value of '{directive.Text}' inside a mapping must be a mapping. To insert list items, put the directive on a list item: '- {directive.Text}:'.");
        }
    }

    private IReadOnlyList<YamlNode> ExpandDirectiveValueAsItems(Directive directive, YamlNode valueNode, AzureTemplateScope scope, string file, string? containerKey)
    {
        switch (valueNode)
        {
            case YamlSequenceNode sequence:
                return ExpandSequence(sequence, scope, file, containerKey, null).Children.ToList();

            case YamlMappingNode mapping:
            {
                // A single item (possibly itself made of directives): expand it as a one-item list
                var wrapper = new YamlSequenceNode();
                wrapper.Add(mapping);
                _origins[wrapper] = OriginOf(mapping, file);
                return ExpandSequence(wrapper, scope, file, containerKey, null).Children.ToList();
            }

            case YamlScalarNode scalar:
            {
                var text = scalar.Value ?? string.Empty;
                if (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && AzureTemplateValues.IsNullLiteral(text))
                {
                    return Array.Empty<YamlNode>();
                }

                if (IsWholeExpression(text, out var expression))
                {
                    var value = Evaluate(expression, scope, scalar, file);
                    return value switch
                    {
                        null => Array.Empty<YamlNode>(),
                        IReadOnlyList<object?> list => list.Select(element => ValueToNode(element, scalar, file)).ToList(),
                        IReadOnlyDictionary<string, object?> => new[] { ValueToNode(value, scalar, file) },
                        _ => new YamlNode[] { ScalarFromValue(value, expression, scalar, file) }
                    };
                }

                throw TemplateError(file, scalar, $"The value of '{directive.Text}' on a list item must be a list of items to insert, not a scalar.");
            }

            default:
                throw TemplateError(file, valueNode, $"The value of '{directive.Text}' on a list item must be a list of items to insert.");
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Expression evaluation
    // ---------------------------------------------------------------------------------------------------------

    private object? Evaluate(string expression, AzureTemplateScope scope, YamlNode at, string file)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            throw TemplateError(file, at, "Template expression '${{ }}' is empty.", ExpressionSuggestions);
        }

        ExpressionNode ast;
        try
        {
            ast = ExpressionParser.Parse(trimmed);
        }
        catch (ExpressionException ex)
        {
            throw ExpressionError(trimmed, ex, at, file);
        }

        ValidateContexts(ast, scope, trimmed, at, file);

        try
        {
            return ExpressionEvaluator.Evaluate(ast, scope.CreateContext(_workspace), trimmed);
        }
        catch (ExpressionException ex)
        {
            throw ExpressionError(trimmed, ex, at, file);
        }
    }

    private PipelineParseException ExpressionError(string expression, ExpressionException exception, YamlNode at, string file)
    {
        var message = exception.Message;
        var separator = message.IndexOf("': ", StringComparison.Ordinal);
        var reason = separator >= 0 ? message[(separator + 3)..] : message;

        return TemplateError(file, at, $"Template expression '${{{{ {expression} }}}}' could not be evaluated: {reason}.", ExpressionSuggestions, exception);
    }

    /// <summary>
    /// Rejects references that are not available at template expansion time (unknown contexts, undeclared
    /// parameters, status functions) with a clear message instead of a silent empty value.
    /// </summary>
    private void ValidateContexts(ExpressionNode node, AzureTemplateScope scope, string expression, YamlNode at, string file)
    {
        switch (node)
        {
            case ContextAccessNode access:
                ValidateRoot(access.Root, access.Segments, scope, expression, at, file);
                ValidateSegments(access.Segments, scope, expression, at, file);
                break;

            case FunctionCallNode call:
                if (RuntimeOnlyFunctions.Contains(call.Name))
                {
                    throw TemplateError(
                        file,
                        at,
                        $"Template expression '${{{{ {expression} }}}}' calls {call.Name}(), which is only available at run time.",
                        new[] { "Use 'condition:' (or a $[ ] runtime expression) for status checks; template expressions are evaluated before the pipeline runs" });
                }

                foreach (var argument in call.Arguments)
                {
                    ValidateContexts(argument, scope, expression, at, file);
                }

                break;

            case BinaryNode binary:
                ValidateContexts(binary.Left, scope, expression, at, file);
                ValidateContexts(binary.Right, scope, expression, at, file);
                break;

            case NotNode not:
                ValidateContexts(not.Operand, scope, expression, at, file);
                break;

            case MemberAccessNode member:
                ValidateContexts(member.Target, scope, expression, at, file);
                ValidateSegments(member.Segments, scope, expression, at, file);
                break;
        }
    }

    private void ValidateSegments(IReadOnlyList<AccessSegment> segments, AzureTemplateScope scope, string expression, YamlNode at, string file)
    {
        foreach (var segment in segments)
        {
            if (segment is IndexSegment index)
            {
                ValidateContexts(index.Index, scope, expression, at, file);
            }
        }
    }

    private void ValidateRoot(string root, IReadOnlyList<AccessSegment> segments, AzureTemplateScope scope, string expression, YamlNode at, string file)
    {
        if (root.Equals("parameters", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetFirstName(segments, out var name) && !scope.Parameters.ContainsKey(name))
            {
                var declared = scope.Parameters.Count == 0
                    ? "the file declares no parameters"
                    : $"declared parameters: {string.Join(", ", scope.Parameters.Keys)}";

                throw TemplateError(
                    file,
                    at,
                    $"Template expression '${{{{ {expression} }}}}' references parameter '{name}', which is not declared ({declared}).",
                    new[]
                    {
                        $"Declare it in the 'parameters:' block of {DisplayName(scope.File)}",
                        "Parameters of the pipeline can be given a value with --param name=value"
                    });
            }

            return;
        }

        if (root.Equals("variables", StringComparison.OrdinalIgnoreCase) || scope.IsLoopVariable(root))
        {
            return;
        }

        throw TemplateError(
            file,
            at,
            $"Template expression '${{{{ {expression} }}}}' uses '{root}', which is not available when templates are expanded.",
            ExpressionSuggestions);
    }

    private static bool TryGetFirstName(IReadOnlyList<AccessSegment> segments, out string name)
    {
        if (segments.Count > 0)
        {
            switch (segments[0])
            {
                case PropertySegment property:
                    name = property.Name;
                    return true;
                case IndexSegment { Index: LiteralNode { Value: string literal } }:
                    name = literal;
                    return true;
            }
        }

        name = string.Empty;
        return false;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Node construction and bookkeeping
    // ---------------------------------------------------------------------------------------------------------

    private YamlNode ValueToNode(object? value, YamlNode source, string file)
    {
        switch (value)
        {
            case null:
                return NewScalar(string.Empty, ScalarStyle.Plain, source, file);

            case bool boolean:
                return NewScalar(boolean ? "true" : "false", ScalarStyle.Plain, source, file);

            case double number:
                return NewScalar(ExpressionValue.FormatNumber(number), ScalarStyle.Plain, source, file);

            case string text:
                return NewScalar(text, NeedsQuoting(text) ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain, source, file);

            case IReadOnlyDictionary<string, object?> mapping:
            {
                var node = NewMapping(source, file);
                foreach (var (key, entry) in mapping)
                {
                    node.Add(NewScalar(key, NeedsQuoting(key) ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain, source, file), ValueToNode(entry, source, file));
                }

                return node;
            }

            case IReadOnlyList<object?> list:
            {
                var node = NewSequence(source, file);
                foreach (var element in list)
                {
                    node.Add(ValueToNode(element, source, file));
                }

                return node;
            }

            default:
                return NewScalar(value.ToString() ?? string.Empty, ScalarStyle.Plain, source, file);
        }
    }

    private YamlScalarNode NewScalar(string value, ScalarStyle style, YamlNode source, string file)
    {
        CountNode();
        var node = new YamlScalarNode(value) { Style = style };
        _origins[node] = OriginOf(source, file);
        return node;
    }

    private YamlMappingNode NewMapping(YamlNode source, string file)
    {
        CountNode();
        var node = new YamlMappingNode { Style = MappingStyle.Block };
        _origins[node] = OriginOf(source, file);
        return node;
    }

    private YamlSequenceNode NewSequence(YamlNode source, string file)
    {
        CountNode();
        var node = new YamlSequenceNode { Style = SequenceStyle.Block };
        _origins[node] = OriginOf(source, file);
        return node;
    }

    /// <summary>Records the origin of an input node that is carried over unchanged into the output.</summary>
    private void Reuse(YamlNode node, string file)
    {
        CountNode();
        if (!_origins.ContainsKey(node))
        {
            _origins[node] = new YamlNodeOrigin(file, node.Start, node.End);
        }
    }

    private void CountNode()
    {
        if (++_nodeCount > MaxNodes)
        {
            throw new PipelineParseException(
                ErrorCodes.InvalidPipelineStructure,
                $"Template expansion produced more than {MaxNodes} nodes; the pipeline is too large to expand locally.",
                new ErrorContext { PipelineFile = _includeChain.Count > 0 ? _includeChain[0].File : null },
                new[] { "Check ${{ each }} loops and nested templates for runaway expansion" });
        }
    }

    private YamlNodeOrigin OriginOf(YamlNode node, string file) =>
        _origins.TryGetValue(node, out var origin) ? origin : new YamlNodeOrigin(file, node.Start, node.End);

    private void Merge(YamlMappingNode target, HashSet<string> keys, YamlMappingNode fragment, string file, Action<string, YamlNode>? entryProduced)
    {
        foreach (var (key, value) in fragment.Children)
        {
            AddEntry(target, keys, (YamlScalarNode)key, value, file, entryProduced);
        }
    }

    private PipelineParseException DuplicateKey(string file, YamlNode keyNode, string keyText) =>
        TemplateError(
            file,
            keyNode,
            $"Duplicate key '{keyText}': it is already defined in this mapping (a template expression may have inserted it a second time).",
            new[] { "Remove one of the definitions, or move the key into the ${{ if }} / ${{ else }} branches so that only one is inserted" });

    private void AddEntry(YamlMappingNode target, HashSet<string> keys, YamlScalarNode keyNode, YamlNode value, string file, Action<string, YamlNode>? entryProduced)
    {
        var keyText = keyNode.Value ?? string.Empty;
        if (!keys.Add(keyText))
        {
            throw DuplicateKey(file, keyNode, keyText);
        }

        target.Add(keyNode, value);
        entryProduced?.Invoke(keyText, value);
    }

    private static string KeyText(YamlNode keyNode, string file)
    {
        if (keyNode is YamlScalarNode scalar)
        {
            return scalar.Value ?? string.Empty;
        }

        throw new PipelineParseException(
            ErrorCodes.InvalidPipelineStructure,
            $"Invalid YAML structure in {Path.GetFileName(file)} at line {keyNode.Start.Line}: mapping keys must be scalars.",
            ErrorContext.FromParserPosition(file, keyNode.Start.Line, keyNode.Start.Column));
    }

    private static bool TryGetEntry(YamlMappingNode mapping, string key, out YamlNode value)
    {
        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            if (keyNode is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                value = valueNode;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static string DescribeNode(YamlNode node) => node switch
    {
        YamlScalarNode => "a scalar value",
        YamlSequenceNode => "a list",
        YamlMappingNode => "a mapping",
        _ => node.GetType().Name
    };

    private static string Truncate(string text) => text.Length <= 60 ? text : text[..57] + "...";

    // ---------------------------------------------------------------------------------------------------------
    // Errors
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Builds a structure error located at <paramref name="node"/> (in <paramref name="file"/> unless the node was synthesized elsewhere).</summary>
    private PipelineParseException TemplateError(string file, YamlNode? node, string message, IEnumerable<string>? suggestions = null, Exception? innerException = null)
    {
        var origin = node is null ? new YamlNodeOrigin(file, Mark.Empty, Mark.Empty) : OriginOf(node, file);
        var line = origin.Start.Line;
        var column = origin.Start.Column;

        var location = line > 0
            ? $" (line {line} in {DisplayName(origin.File)}{IncludeChainSuffix(origin.File)})"
            : $" (in {DisplayName(origin.File)}{IncludeChainSuffix(origin.File)})";

        return new PipelineParseException(
            ErrorCodes.InvalidPipelineStructure,
            message + location,
            ErrorContext.FromParserPosition(origin.File, line, column),
            suggestions,
            innerException);
    }

    private string IncludeChainSuffix(string file)
    {
        var frame = _includeChain.LastOrDefault(f => PathEquals(f.File, file));
        if (frame?.ReferencedFrom is null)
        {
            return string.Empty;
        }

        return $", included from {DisplayName(frame.ReferencedFrom)} line {frame.ReferenceLine}";
    }

    /// <summary>A short, readable name for a file: relative to the pipeline directory when it lives under it.</summary>
    private string DisplayName(string file)
    {
        if (file == InlineContentName)
        {
            return file;
        }

        try
        {
            var full = Path.GetFullPath(file);
            if (!string.IsNullOrEmpty(_rootDirectory) && full.StartsWith(_rootDirectory, PathComparison))
            {
                var relative = Path.GetRelativePath(_rootDirectory, full);
                if (!relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return relative.Replace('\\', '/');
                }
            }

            return Path.GetFileName(full);
        }
        catch (ArgumentException)
        {
            return file;
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool PathEquals(string left, string right) => string.Equals(left, right, PathComparison);
}
