using PDK.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PDK.Providers.AzureDevOps.Templates;

/// <summary>
/// Template files (<c>- template: file.yml</c> in steps/jobs/stages/variables lists) and <c>extends:</c>.
/// </summary>
public sealed partial class AzureTemplateProcessor
{
    private static readonly string[] HierarchyKeys = { "stages", "jobs", "steps" };

    private static readonly string[] OtherRepositorySuggestions =
    {
        "Copy (vendor) the template file into this repository and reference it with a relative path, or with '@self'",
        "Only templates from the repository being built (the 'self' repository resource) can be resolved locally"
    };

    private static readonly string[] NotFoundSuggestions =
    {
        "Template paths are relative to the file that references them; a path starting with '/' is relative to the workspace root",
        "Templates from other repositories are not supported; vendor the file into this repository"
    };

    /// <summary>
    /// Includes a template referenced from a <paramref name="containerKey"/> list and returns the items to splice
    /// in place of the reference.
    /// </summary>
    private IReadOnlyList<YamlNode> IncludeTemplate(YamlMappingNode reference, AzureTemplateScope scope, string file, string containerKey)
    {
        var (templateNode, parametersNode) = ReadTemplateReference(reference, file);
        var referenceText = TemplateReferenceText(templateNode, scope, file);
        var fullPath = ResolveTemplatePath(referenceText, templateNode, file);
        var document = LoadTemplateDocument(fullPath, referenceText, templateNode, file);

        var declarations = ReadParameterDeclarations(document, fullPath);
        var supplied = ReadSuppliedParameters(parametersNode, scope, file);
        var parameters = BindTemplateParameters(declarations, supplied, referenceText, fullPath, templateNode, file);
        var templateScope = scope.ForTemplate(fullPath, parameters);

        _includeChain.Add(new IncludeFrame(fullPath, file, OriginOf(templateNode, file).Start.Line));
        try
        {
            if (!TryGetEntry(document, containerKey, out var section))
            {
                throw TemplateError(
                    fullPath,
                    document,
                    $"Template '{referenceText}' is referenced from a '{containerKey}' list but does not define a top-level '{containerKey}' section.",
                    new[] { $"A template used in '{containerKey}:' must define '{containerKey}:' at its top level (plus an optional 'parameters:' block)" });
            }

            return ExpandTemplateSection(section, templateScope, fullPath, containerKey, referenceText);
        }
        finally
        {
            _includeChain.RemoveAt(_includeChain.Count - 1);
        }
    }

    private IReadOnlyList<YamlNode> ExpandTemplateSection(YamlNode section, AzureTemplateScope scope, string file, string containerKey, string referenceText)
    {
        if (containerKey == "variables")
        {
            var expanded = ExpandVariablesSection(section, scope, file);
            return expanded switch
            {
                YamlSequenceNode sequence => sequence.Children.ToList(),
                YamlMappingNode mapping => MappingToVariableItems(mapping, file),
                _ => throw TemplateError(file, section, $"The 'variables' section of template '{referenceText}' must be a mapping or a list.")
            };
        }

        switch (section)
        {
            case YamlSequenceNode sequence:
                return ExpandSequence(sequence, scope, file, containerKey, null).Children.ToList();

            case YamlScalarNode scalar:
            {
                var expanded = ExpandScalar(scalar, scope, file);
                if (expanded is YamlSequenceNode expandedSequence)
                {
                    return expandedSequence.Children.ToList();
                }

                break;
            }
        }

        throw TemplateError(file, section, $"The '{containerKey}' section of template '{referenceText}' must be a list.");
    }

    /// <summary>Converts a mapping-form <c>variables:</c> block into <c>- name/value</c> items.</summary>
    private List<YamlNode> MappingToVariableItems(YamlMappingNode mapping, string file)
    {
        var items = new List<YamlNode>(mapping.Children.Count);
        foreach (var (key, value) in mapping.Children)
        {
            var item = NewMapping(key, file);
            item.Add(NewScalar("name", ScalarStyle.Plain, key, file), key);
            item.Add(NewScalar("value", ScalarStyle.Plain, key, file), value);
            items.Add(item);
        }

        return items;
    }

    /// <summary>Reads a <c>template:</c> reference mapping, which may only contain <c>template</c> and <c>parameters</c>.</summary>
    private (YamlNode Template, YamlNode? Parameters) ReadTemplateReference(YamlMappingNode reference, string file)
    {
        YamlNode? templateNode = null;
        YamlNode? parametersNode = null;

        foreach (var (keyNode, valueNode) in reference.Children)
        {
            var key = KeyText(keyNode, file);
            switch (key)
            {
                case "template":
                    templateNode = valueNode;
                    break;
                case "parameters":
                    parametersNode = valueNode;
                    break;
                default:
                    throw TemplateError(
                        file,
                        keyNode,
                        $"A template reference may only contain 'template' and 'parameters', but it also defines '{key}'.",
                        new[] { "Move the extra keys into the template file, or pass them as parameters" });
            }
        }

        return (templateNode!, parametersNode);
    }

    private string TemplateReferenceText(YamlNode templateNode, AzureTemplateScope scope, string file)
    {
        if (templateNode is YamlScalarNode scalar && ExpandScalar(scalar, scope, file) is YamlScalarNode expanded && !string.IsNullOrWhiteSpace(expanded.Value))
        {
            return expanded.Value.Trim();
        }

        throw TemplateError(file, templateNode, "'template:' must be the path of a template file, e.g. template: templates/build-steps.yml.");
    }

    /// <summary>
    /// Resolves a template path. <c>@self</c> is accepted; any other repository alias is rejected because the
    /// file cannot be fetched locally.
    /// </summary>
    private string ResolveTemplatePath(string referenceText, YamlNode referenceNode, string file)
    {
        var path = referenceText;
        var at = referenceText.LastIndexOf('@');
        if (at >= 0)
        {
            var repository = referenceText[(at + 1)..].Trim();
            path = referenceText[..at];

            if (!repository.Equals("self", StringComparison.OrdinalIgnoreCase))
            {
                throw TemplateError(
                    file,
                    referenceNode,
                    $"Template '{referenceText}' refers to repository resource '{repository}': templates from other repositories are not supported.",
                    OtherRepositorySuggestions);
            }
        }

        path = path.Trim().Replace('\\', '/');
        if (path.Length == 0)
        {
            throw TemplateError(file, referenceNode, $"Template reference '{referenceText}' has no file path.");
        }

        var basePath = path.StartsWith('/') ? _workspace : DirectoryOf(file);
        var relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(basePath, relative));
    }

    private string DirectoryOf(string file)
    {
        if (file == InlineContentName)
        {
            return _rootDirectory;
        }

        return Path.GetDirectoryName(Path.GetFullPath(file)) ?? _rootDirectory;
    }

    /// <summary>Loads (and caches) a template file, checking for cycles, nesting depth and existence.</summary>
    private YamlMappingNode LoadTemplateDocument(string fullPath, string referenceText, YamlNode referenceNode, string file)
    {
        var cycleStart = _includeChain.FindIndex(frame => PathEquals(frame.File, fullPath));
        if (cycleStart >= 0)
        {
            var chain = _includeChain.Skip(cycleStart).Select(frame => DisplayName(frame.File)).Append(DisplayName(fullPath));
            throw TemplateError(
                file,
                referenceNode,
                $"Template include cycle detected: {string.Join(" -> ", chain)}.",
                new[] { "A template cannot include itself, directly or through other templates" });
        }

        if (_includeChain.Count >= MaxIncludeDepth)
        {
            var chain = _includeChain.Select(frame => DisplayName(frame.File)).Append(DisplayName(fullPath));
            throw TemplateError(
                file,
                referenceNode,
                $"Templates are nested more than {MaxIncludeDepth} levels deep: {string.Join(" -> ", chain)}.",
                new[] { "Flatten the template hierarchy" });
        }

        if (_documents.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        var content = _readFile(fullPath);
        if (content is null)
        {
            throw TemplateError(
                file,
                referenceNode,
                $"Template file '{referenceText}' was not found (resolved to '{fullPath}').",
                NotFoundSuggestions);
        }

        var document = LoadDocument(content, fullPath);
        _documents[fullPath] = document;
        return document;
    }

    /// <summary>
    /// Reads the <c>parameters:</c> mapping of a template reference, expanding expressions with the caller's scope.
    /// </summary>
    private Dictionary<string, object?> ReadSuppliedParameters(YamlNode? parametersNode, AzureTemplateScope scope, string file)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parametersNode is null)
        {
            return result;
        }

        if (parametersNode is YamlScalarNode scalar && scalar.Style is ScalarStyle.Plain or ScalarStyle.Any && AzureTemplateValues.IsNullLiteral(scalar.Value))
        {
            return result;
        }

        var expanded = ExpandNode(parametersNode, scope, file, "parameters");
        if (expanded is not YamlMappingNode mapping)
        {
            throw TemplateError(
                file,
                parametersNode,
                "'parameters' of a template reference must be a mapping of parameter names to values.",
                new[] { "Example: parameters: { configuration: Release, runTests: true }" });
        }

        foreach (var (key, value) in mapping.Children)
        {
            result[KeyText(key, file)] = AzureTemplateValues.ToValue(value);
        }

        return result;
    }

    /// <summary>
    /// Expands an <c>extends:</c> pipeline: the template becomes the pipeline and the extending file's own keys
    /// (name, trigger, pr, schedules, resources, pool, variables, ...) are applied on top of it. Variables of both
    /// files are merged, with the extending file's values winning.
    /// </summary>
    private YamlMappingNode ExpandExtends(YamlMappingNode root, YamlNode extendsNode, AzureTemplateScope scope, string file)
    {
        foreach (var key in HierarchyKeys)
        {
            if (TryGetEntry(root, key, out var conflicting))
            {
                throw TemplateError(
                    file,
                    conflicting,
                    $"A pipeline that uses 'extends' cannot also define '{key}'; the extended template provides the stages, jobs or steps.",
                    new[] { $"Move '{key}' into the template, or remove 'extends'" });
            }
        }

        if (extendsNode is not YamlMappingNode extendsMapping)
        {
            throw TemplateError(
                file,
                extendsNode,
                "'extends' must be a mapping with 'template' (and optionally 'parameters').",
                new[] { "Example: extends: { template: templates/pipeline.yml, parameters: { environment: staging } }" });
        }

        // The extending file's own keys come first so that its variables are visible to the template
        var rootExpanded = ExpandMapping(root, scope, file, null, ExtendsRootSkipKeys, null);

        var (templateNode, parametersNode) = ReadTemplateReference(extendsMapping, file);
        var referenceText = TemplateReferenceText(templateNode, scope, file);
        var fullPath = ResolveTemplatePath(referenceText, templateNode, file);
        var document = LoadTemplateDocument(fullPath, referenceText, templateNode, file);

        var declarations = ReadParameterDeclarations(document, fullPath);
        var supplied = ReadSuppliedParameters(parametersNode, scope, file);
        var parameters = BindTemplateParameters(declarations, supplied, referenceText, fullPath, templateNode, file);
        var templateScope = scope.ForTemplate(fullPath, parameters);

        YamlMappingNode templateExpanded;
        _includeChain.Add(new IncludeFrame(fullPath, file, OriginOf(templateNode, file).Start.Line));
        try
        {
            if (TryGetEntry(document, "extends", out var nested))
            {
                throw TemplateError(fullPath, nested, $"Template '{referenceText}' uses 'extends' itself; an extended template cannot extend another template.");
            }

            templateExpanded = ExpandMapping(document, templateScope, fullPath, null, RootSkipKeys, null);
        }
        finally
        {
            _includeChain.RemoveAt(_includeChain.Count - 1);
        }

        return MergeExtendedPipeline(templateExpanded, rootExpanded, document, fullPath);
    }

    private YamlMappingNode MergeExtendedPipeline(YamlMappingNode template, YamlMappingNode root, YamlNode templateDocument, string templateFile)
    {
        var entries = new List<(string Key, YamlScalarNode KeyNode, YamlNode Value)>();
        YamlNode? templateVariables = null;
        YamlNode? rootVariables = null;

        foreach (var (key, value) in template.Children)
        {
            var keyText = AzureTemplateValues.KeyText(key);
            if (keyText == "variables")
            {
                templateVariables = value;
                continue;
            }

            entries.Add((keyText, (YamlScalarNode)key, value));
        }

        foreach (var (key, value) in root.Children)
        {
            var keyText = AzureTemplateValues.KeyText(key);
            if (keyText == "variables")
            {
                rootVariables = value;
                continue;
            }

            var index = entries.FindIndex(entry => entry.Key == keyText);
            if (index >= 0)
            {
                entries[index] = (keyText, (YamlScalarNode)key, value);
            }
            else
            {
                entries.Add((keyText, (YamlScalarNode)key, value));
            }
        }

        var variables = MergeVariables(templateVariables, rootVariables, templateFile);
        if (variables is not null)
        {
            entries.Add(("variables", NewScalar("variables", ScalarStyle.Plain, variables, templateFile), variables));
        }

        var merged = NewMapping(templateDocument, templateFile);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, keyNode, value) in entries)
        {
            AddEntry(merged, keys, keyNode, value, templateFile, null);
        }

        return merged;
    }

    private YamlNode? MergeVariables(YamlNode? templateVariables, YamlNode? rootVariables, string templateFile)
    {
        if (templateVariables is null)
        {
            return rootVariables;
        }

        if (rootVariables is null)
        {
            return templateVariables;
        }

        var merged = NewSequence(templateVariables, templateFile);
        foreach (var item in VariableItems(templateVariables, templateFile).Concat(VariableItems(rootVariables, templateFile)))
        {
            merged.Add(item);
        }

        return merged;
    }

    private IEnumerable<YamlNode> VariableItems(YamlNode variables, string file) => variables switch
    {
        YamlSequenceNode sequence => sequence.Children,
        YamlMappingNode mapping => MappingToVariableItems(mapping, file),
        _ => Array.Empty<YamlNode>()
    };
}
