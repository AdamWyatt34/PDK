using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;

namespace PDK.Core.Expressions;

/// <summary>
/// Evaluates parsed pipeline expressions against an <see cref="ExpressionContext"/>.
/// Implements the GitHub Actions operators and functions and the Azure Pipelines function set.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>Parses and evaluates <paramref name="expression"/>.</summary>
    public static object? Evaluate(string expression, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var node = ExpressionParser.Parse(expression);
        return Evaluate(node, context, expression);
    }

    /// <summary>
    /// Evaluates a condition. A null or blank condition uses the dialect default
    /// (<c>success()</c> / <c>succeeded()</c>), i.e. it runs only when nothing failed so far.
    /// </summary>
    public static bool EvaluateCondition(string? condition, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var text = Unwrap(condition);
        if (string.IsNullOrWhiteSpace(text))
        {
            return context.Status == ExpressionJobStatus.Success;
        }

        // GitHub adds success() to any condition that does not itself check the status,
        // so `if: github.ref == 'x'` still does not run after a failure. Azure does not.
        if (context.Syntax == ExpressionSyntax.GitHub &&
            context.Status != ExpressionJobStatus.Success &&
            !ContainsStatusFunction(text))
        {
            return false;
        }

        return ExpressionValue.IsTruthy(Evaluate(text, context));
    }

    private static readonly System.Text.RegularExpressions.Regex StatusFunctionPattern = new(
        @"\b(success|failure|cancelled|canceled|always|succeeded|failed|succeededOrFailed)\s*\(",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    /// <summary>Whether the condition text calls one of the status functions.</summary>
    public static bool ContainsStatusFunction(string? condition) =>
        !string.IsNullOrEmpty(condition) && StatusFunctionPattern.IsMatch(condition);

    /// <summary>
    /// Strips a surrounding <c>${{ }}</c> (or Azure <c>$[ ]</c>) wrapper from a condition so that
    /// <c>if: ${{ always() }}</c> and <c>if: always()</c> evaluate the same way.
    /// </summary>
    public static string Unwrap(string? condition)
    {
        if (condition == null)
        {
            return string.Empty;
        }

        var text = condition.Trim();
        if (text.StartsWith("${{", StringComparison.Ordinal) && text.EndsWith("}}", StringComparison.Ordinal))
        {
            return text[3..^2].Trim();
        }

        if (text.StartsWith("$[", StringComparison.Ordinal) && text.EndsWith(']'))
        {
            return text[2..^1].Trim();
        }

        return text;
    }

    /// <summary>Evaluates an already parsed node.</summary>
    public static object? Evaluate(ExpressionNode node, ExpressionContext context, string source)
    {
        switch (node)
        {
            case LiteralNode literal:
                return literal.Value;

            case NotNode not:
                return !ExpressionValue.IsTruthy(Evaluate(not.Operand, context, source));

            case BinaryNode binary:
                return EvaluateBinary(binary, context, source);

            case ContextAccessNode access:
                return EvaluateAccess(access, context, source);

            case FunctionCallNode call:
                return EvaluateFunction(call, context, source);

            case MemberAccessNode member:
                return ApplySegments(Evaluate(member.Target, context, source), member.Segments, context, source);

            default:
                throw new ExpressionException(source, $"unsupported node {node.GetType().Name}");
        }
    }

    private static object? EvaluateBinary(BinaryNode node, ExpressionContext context, string source)
    {
        switch (node.Operator)
        {
            case "&&":
            {
                var left = Evaluate(node.Left, context, source);
                return ExpressionValue.IsTruthy(left) ? Evaluate(node.Right, context, source) : left;
            }
            case "||":
            {
                var left = Evaluate(node.Left, context, source);
                return ExpressionValue.IsTruthy(left) ? left : Evaluate(node.Right, context, source);
            }
        }

        var l = Evaluate(node.Left, context, source);
        var r = Evaluate(node.Right, context, source);
        switch (node.Operator)
        {
            case "==":
                return ExpressionValue.LooseEquals(l, r);
            case "!=":
                return !ExpressionValue.LooseEquals(l, r);
            case "<":
                return ExpressionValue.Compare(l, r) is < 0;
            case "<=":
                return ExpressionValue.Compare(l, r) is <= 0;
            case ">":
                return ExpressionValue.Compare(l, r) is > 0;
            case ">=":
                return ExpressionValue.Compare(l, r) is >= 0;
            default:
                throw new ExpressionException(source, $"unsupported operator '{node.Operator}'");
        }
    }

    private static object? EvaluateAccess(ContextAccessNode node, ExpressionContext context, string source)
    {
        if (!context.HasRoot(node.Root))
        {
            // GitHub treats an unknown context as an error; we resolve to null so that a missing
            // context (e.g. `inputs` outside workflow_dispatch) reads as empty rather than aborting.
            if (node.Segments.Count == 0)
            {
                // A bare identifier: may be an Azure keyword written without quotes (e.g. `succeeded`) — reject clearly.
                throw new ExpressionException(source, $"unknown context or value '{node.Root}'");
            }

            return null;
        }

        return ApplySegments(context.GetRoot(node.Root), node.Segments, context, source);
    }

    private static object? ApplySegments(object? current, IReadOnlyList<AccessSegment> segments, ExpressionContext context, string source)
    {
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case PropertySegment prop:
                    current = ExpressionValue.GetProperty(current, prop.Name);
                    break;

                case IndexSegment index:
                {
                    var key = Evaluate(index.Index, context, source);
                    if (current is IReadOnlyList<object?> list && key is double d)
                    {
                        var i = (int)d;
                        current = i >= 0 && i < list.Count ? list[i] : null;
                    }
                    else
                    {
                        current = ExpressionValue.GetProperty(current, ExpressionValue.ToText(key));
                    }

                    break;
                }

                case WildcardSegment:
                    current = current switch
                    {
                        IReadOnlyDictionary<string, object?> dict => dict.Values.ToList(),
                        IReadOnlyList<object?> list => list,
                        _ => new List<object?>()
                    };
                    break;
            }

            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    private static object? EvaluateFunction(FunctionCallNode call, ExpressionContext context, string source)
    {
        var name = call.Name.ToLowerInvariant();
        var args = call.Arguments;

        object? Arg(int i) => i < args.Count ? Evaluate(args[i], context, source) : null;
        string Str(int i) => ExpressionValue.ToText(Arg(i));
        void Arity(int min, int max = int.MaxValue)
        {
            if (args.Count < min || args.Count > max)
            {
                throw new ExpressionException(source, $"{call.Name}() expects {(min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min}..{(max == int.MaxValue ? "n" : max.ToString(CultureInfo.InvariantCulture))}")} argument(s) but got {args.Count}");
            }
        }

        switch (name)
        {
            // ---- status functions (both dialects) ----
            case "success":
            case "succeeded":
                Arity(0, int.MaxValue);
                return context.Status == ExpressionJobStatus.Success;
            case "failure":
            case "failed":
                Arity(0, int.MaxValue);
                return context.Status == ExpressionJobStatus.Failure;
            case "always":
                return true;
            case "cancelled":
            case "canceled":
                return context.Status == ExpressionJobStatus.Cancelled;
            case "succeededorfailed":
                return context.Status != ExpressionJobStatus.Cancelled;

            // ---- GitHub functions ----
            case "contains":
            {
                Arity(2, 2);
                var search = Arg(0);
                var item = Arg(1);
                if (search is IReadOnlyList<object?> list)
                {
                    return list.Any(v => ExpressionValue.LooseEquals(v, item));
                }

                return ExpressionValue.ToText(search).Contains(ExpressionValue.ToText(item), StringComparison.OrdinalIgnoreCase);
            }
            case "containsvalue":
            {
                Arity(2, 2);
                var target = Arg(0);
                var item = Arg(1);
                return target switch
                {
                    IReadOnlyDictionary<string, object?> dict => dict.Values.Any(v => ExpressionValue.AzureEquals(v, item)),
                    IReadOnlyList<object?> list => list.Any(v => ExpressionValue.AzureEquals(v, item)),
                    _ => false
                };
            }
            case "startswith":
                Arity(2, 2);
                return Str(0).StartsWith(Str(1), StringComparison.OrdinalIgnoreCase);
            case "endswith":
                Arity(2, 2);
                return Str(0).EndsWith(Str(1), StringComparison.OrdinalIgnoreCase);
            case "format":
            {
                Arity(1);
                var values = new List<object?>();
                for (var i = 1; i < args.Count; i++)
                {
                    values.Add(Arg(i));
                }

                return Format(Str(0), values, source);
            }
            case "join":
            {
                Arity(1, 2);
                var first = Arg(0);
                var second = args.Count > 1 ? Arg(1) : null;

                // Azure writes join(separator, list); GitHub writes join(list, separator)
                if (first is not IReadOnlyList<object?> && second is IReadOnlyList<object?> azureList)
                {
                    return string.Join(ExpressionValue.ToText(first), azureList.Select(ExpressionValue.ToText));
                }

                var separator = args.Count > 1 ? ExpressionValue.ToText(second) : ",";
                return first is IReadOnlyList<object?> list
                    ? string.Join(separator, list.Select(ExpressionValue.ToText))
                    : ExpressionValue.ToText(first);
            }
            case "tojson":
            case "converttojson":
                Arity(1, 1);
                return ExpressionValue.ToJson(Arg(0));
            case "fromjson":
            {
                Arity(1, 1);
                var text = Str(0);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                try
                {
                    return ExpressionValue.FromJson(text);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new ExpressionException(source, $"fromJSON(): {ex.Message}");
                }
            }
            case "hashfiles":
            {
                Arity(1);
                var patterns = Enumerable.Range(0, args.Count).Select(Str).ToList();
                return HashFiles(context.Workspace, patterns);
            }

            // ---- Azure functions ----
            case "eq":
                Arity(2, 2);
                return ExpressionValue.AzureEquals(Arg(0), Arg(1));
            case "ne":
                Arity(2, 2);
                return !ExpressionValue.AzureEquals(Arg(0), Arg(1));
            case "and":
            {
                Arity(1);
                for (var i = 0; i < args.Count; i++)
                {
                    if (!ExpressionValue.IsTruthy(Arg(i)))
                    {
                        return false;
                    }
                }

                return true;
            }
            case "or":
            {
                Arity(1);
                for (var i = 0; i < args.Count; i++)
                {
                    if (ExpressionValue.IsTruthy(Arg(i)))
                    {
                        return true;
                    }
                }

                return false;
            }
            case "not":
                Arity(1, 1);
                return !ExpressionValue.IsTruthy(Arg(0));
            case "xor":
                Arity(2, 2);
                return ExpressionValue.IsTruthy(Arg(0)) ^ ExpressionValue.IsTruthy(Arg(1));
            case "lt":
                Arity(2, 2);
                return ExpressionValue.Compare(Arg(0), Arg(1)) is < 0;
            case "le":
                Arity(2, 2);
                return ExpressionValue.Compare(Arg(0), Arg(1)) is <= 0;
            case "gt":
                Arity(2, 2);
                return ExpressionValue.Compare(Arg(0), Arg(1)) is > 0;
            case "ge":
                Arity(2, 2);
                return ExpressionValue.Compare(Arg(0), Arg(1)) is >= 0;
            case "in":
            {
                Arity(2);
                var needle = Arg(0);
                for (var i = 1; i < args.Count; i++)
                {
                    if (ExpressionValue.AzureEquals(needle, Arg(i)))
                    {
                        return true;
                    }
                }

                return false;
            }
            case "notin":
            {
                Arity(2);
                var needle = Arg(0);
                for (var i = 1; i < args.Count; i++)
                {
                    if (ExpressionValue.AzureEquals(needle, Arg(i)))
                    {
                        return false;
                    }
                }

                return true;
            }
            case "coalesce":
            {
                for (var i = 0; i < args.Count; i++)
                {
                    var v = Arg(i);
                    if (v != null && !(v is string s && s.Length == 0))
                    {
                        return v;
                    }
                }

                return null;
            }
            case "lower":
                Arity(1, 1);
                return Str(0).ToLowerInvariant();
            case "upper":
                Arity(1, 1);
                return Str(0).ToUpperInvariant();
            case "trim":
                Arity(1, 1);
                return Str(0).Trim();
            case "length":
            {
                Arity(1, 1);
                var v = Arg(0);
                return v switch
                {
                    IReadOnlyList<object?> list => (double)list.Count,
                    IReadOnlyDictionary<string, object?> dict => (double)dict.Count,
                    _ => (double)ExpressionValue.ToText(v).Length
                };
            }
            case "replace":
                Arity(3, 3);
                return Str(0).Replace(Str(1), Str(2), StringComparison.Ordinal);
            case "split":
                Arity(2, 2);
                return Str(0).Split(Str(1)).Select(s => (object?)s).ToList();
            case "counter":
                // counter(prefix, seed): a persistent counter on Azure; locally the seed (or 1) is returned.
                return args.Count > 1 ? ExpressionValue.ToNumber(Arg(1)) : 1d;
            case "iif":
                Arity(3, 3);
                return ExpressionValue.IsTruthy(Arg(0)) ? Arg(1) : Arg(2);

            default:
                throw new ExpressionException(source, $"unknown function '{call.Name}()'");
        }
    }

    private static string Format(string format, List<object?> values, string source)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < format.Length)
        {
            var c = format[i];
            if (c == '{')
            {
                if (i + 1 < format.Length && format[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }

                var close = format.IndexOf('}', i);
                if (close < 0)
                {
                    throw new ExpressionException(source, "format(): unclosed '{'");
                }

                var indexText = format[(i + 1)..close];
                if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= values.Count)
                {
                    throw new ExpressionException(source, $"format(): argument {{{indexText}}} was not supplied");
                }

                sb.Append(ExpressionValue.ToText(values[index]));
                i = close + 1;
                continue;
            }

            if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static string HashFiles(string? workspace, List<string> patterns)
    {
        var root = string.IsNullOrEmpty(workspace) ? Directory.GetCurrentDirectory() : workspace;
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        var matcher = new Matcher(OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        foreach (var pattern in patterns)
        {
            if (pattern.StartsWith('!'))
            {
                matcher.AddExclude(pattern[1..]);
            }
            else
            {
                matcher.AddInclude(pattern);
            }
        }

        var files = matcher.GetResultsInFullPath(root).OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            return string.Empty;
        }

        using var overall = SHA256.Create();
        foreach (var file in files)
        {
            using var stream = File.OpenRead(file);
            var fileHash = SHA256.HashData(stream);
            overall.TransformBlock(fileHash, 0, fileHash.Length, null, 0);
        }

        overall.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(overall.Hash!).ToLowerInvariant();
    }
}
