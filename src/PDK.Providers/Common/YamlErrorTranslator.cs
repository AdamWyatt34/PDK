using System.Text.RegularExpressions;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using YamlDotNet.Core;

namespace PDK.Providers.Common;

/// <summary>
/// Turns YamlDotNet exceptions into <see cref="PipelineParseException"/>s with line/column context, distinguishing
/// genuine YAML syntax errors from values that are valid YAML but have the wrong shape for the pipeline schema.
/// </summary>
public static class YamlErrorTranslator
{
    private static readonly Regex PositionPrefix = new(
        @"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\):\s*",
        RegexOptions.Compiled);

    private static readonly Regex PositionSuffix = new(
        @"\s*\(at Line: \d+, Col: \d+, Idx: \d+\)\.?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ExpectedGot = new(
        @"Expected '(?<expected>\w+)', got '(?<actual>\w+)'",
        RegexOptions.Compiled);

    private static readonly Regex KeyAtLine = new(
        @"^\s*-?\s*(?<key>[A-Za-z0-9_.\-]+)\s*:",
        RegexOptions.Compiled);

    /// <summary>
    /// Translates a YamlDotNet exception raised while deserializing <paramref name="yamlContent"/>.
    /// </summary>
    /// <param name="exception">The YamlDotNet exception.</param>
    /// <param name="yamlContent">The content being parsed (used to name the offending key).</param>
    /// <param name="displayPath">The file path, or a placeholder name for inline content.</param>
    public static PipelineParseException Translate(YamlException exception, string? yamlContent, string displayPath)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var line = exception.Start.Line;
        var column = exception.Start.Column;
        var details = CleanMessage(exception);
        var fileName = Path.GetFileName(displayPath);

        if (IsSyntaxError(exception))
        {
            return PipelineParseException.YamlSyntaxError(
                displayPath,
                line,
                column,
                $"{details} (line {line}, column {column})",
                exception);
        }

        var key = FindEnclosingKey(yamlContent, line, column);
        var subject = key is null ? "invalid value" : $"invalid value for '{key}'";

        return new PipelineParseException(
            ErrorCodes.InvalidPipelineStructure,
            $"Invalid YAML structure in {fileName} at line {line}, column {column}: {subject} - {details}",
            ErrorContext.FromParserPosition(displayPath, line, column),
            new[]
            {
                key is null
                    ? $"Check the value at line {line}; it does not have the shape the pipeline schema expects"
                    : $"Check the value of '{key}' at line {line}; it does not have the shape the pipeline schema expects",
                "Expressions are accepted in most scalar positions, but not where a list or mapping is required",
                $"See line {line}, column {column}"
            },
            exception);
    }

    private static bool IsSyntaxError(YamlException exception) =>
        exception is SyntaxErrorException or SemanticErrorException or AnchorNotFoundException;

    private static string CleanMessage(YamlException exception)
    {
        Exception current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        var message = current.Message ?? string.Empty;
        message = PositionPrefix.Replace(message, string.Empty);
        message = PositionSuffix.Replace(message, string.Empty);
        message = ExpectedGot.Replace(
            message,
            match => $"expected {Describe(match.Groups["expected"].Value)} but found {Describe(match.Groups["actual"].Value)}");

        return message.Trim();
    }

    private static string Describe(string eventName) => eventName switch
    {
        "MappingStart" => "a mapping",
        "MappingEnd" => "the end of a mapping",
        "SequenceStart" => "a list",
        "SequenceEnd" => "the end of a list",
        "Scalar" => "a scalar value",
        "DocumentStart" => "the start of the document",
        "DocumentEnd" => "the end of the document",
        _ => eventName
    };

    /// <summary>
    /// Finds the key whose value is the node starting at (<paramref name="line"/>, <paramref name="column"/>):
    /// a key earlier on the same line, otherwise the nearest key above with a smaller indentation.
    /// </summary>
    private static string? FindEnclosingKey(string? yamlContent, int line, int column)
    {
        if (string.IsNullOrEmpty(yamlContent) || line < 1)
        {
            return null;
        }

        var lines = yamlContent.Split('\n');
        if (line > lines.Length)
        {
            return null;
        }

        var nodeIndent = Math.Max(column - 1, 0);

        // Key on the same line, before the node ("runs-on: { group: x }")
        var sameLine = KeyAtLine.Match(lines[line - 1]);
        if (sameLine.Success && sameLine.Groups["key"].Index < nodeIndent)
        {
            return sameLine.Groups["key"].Value;
        }

        // Otherwise the parent is the nearest key above whose key starts left of the node
        for (var i = line - 2; i >= 0; i--)
        {
            var candidate = lines[i];
            if (string.IsNullOrWhiteSpace(candidate) || candidate.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = KeyAtLine.Match(candidate);
            if (match.Success && match.Groups["key"].Index < nodeIndent)
            {
                return match.Groups["key"].Value;
            }
        }

        return null;
    }
}
