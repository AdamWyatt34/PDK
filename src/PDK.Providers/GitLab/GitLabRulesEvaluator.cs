using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PDK.Providers.GitLab;

/// <summary>
/// Raised when a GitLab <c>rules:if</c> / <c>only:variables</c> expression cannot be parsed or evaluated.
/// </summary>
public sealed class GitLabExpressionException : Exception
{
    /// <summary>Initializes a new instance with a message.</summary>
    public GitLabExpressionException(string message) : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and position.</summary>
    public GitLabExpressionException(string message, int position) : base(message)
    {
        Position = position;
    }

    /// <summary>Initializes a new instance.</summary>
    public GitLabExpressionException() : base("Invalid GitLab expression")
    {
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public GitLabExpressionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Gets the 0-based character offset the error was detected at, or -1.</summary>
    public int Position { get; } = -1;
}

/// <summary>
/// Evaluates GitLab CI/CD rule expressions (<c>rules:if</c>, <c>only:variables</c>, <c>except:variables</c>,
/// <c>workflow:rules:if</c>):
/// <list type="bullet">
/// <item><c>$VAR</c> / <c>${VAR}</c> — truthy when the variable is defined and not empty</item>
/// <item><c>$VAR == "value"</c>, <c>$VAR != 'value'</c>, <c>$VAR == null</c>, <c>$A == $B</c></item>
/// <item><c>$VAR =~ /pattern/i</c>, <c>$VAR !~ /pattern/</c>, <c>$VAR =~ $PATTERN</c> (flags <c>i</c>, <c>m</c>, <c>s</c>)</item>
/// <item><c>&amp;&amp;</c>, <c>||</c> (<c>&amp;&amp;</c> binds tighter) and parentheses</item>
/// </list>
/// Unquoted words are accepted as string literals for leniency.
/// </summary>
public sealed class GitLabRulesEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<string, string?> _resolve;

    /// <summary>Creates an evaluator over a variable dictionary (case-sensitive names, as on GitLab).</summary>
    /// <param name="variables">Variable name → value; undefined names are null.</param>
    public GitLabRulesEvaluator(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _resolve = name => variables.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>Creates an evaluator over a resolver (return null for undefined variables).</summary>
    /// <param name="resolve">Variable resolver.</param>
    public GitLabRulesEvaluator(Func<string, string?> resolve)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    /// <summary>Evaluates an expression against a variable dictionary.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="variables">Variable name → value.</param>
    /// <returns>The truth value of the expression.</returns>
    /// <exception cref="GitLabExpressionException">The expression is invalid.</exception>
    public static bool Evaluate(string expression, IReadOnlyDictionary<string, string> variables) =>
        new GitLabRulesEvaluator(variables).Evaluate(expression);

    /// <summary>Evaluates an expression.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The truth value of the expression; an empty expression is true (a rule without conditions always matches).</returns>
    /// <exception cref="GitLabExpressionException">The expression is invalid.</exception>
    public bool Evaluate(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        var tokens = Tokenizer.Tokenize(expression);
        var parser = new Parser(tokens, expression);
        var node = parser.ParseExpression();
        return IsTruthy(node.Evaluate(_resolve));
    }

    /// <summary>Truthiness as GitLab defines it: booleans as-is, strings when non-empty, null never.</summary>
    internal static bool IsTruthy(object? value) => value switch
    {
        bool b => b,
        string s => s.Length > 0,
        _ => false
    };

    /// <summary>Parses the <c>/pattern/flags</c> form into a .NET regex.</summary>
    internal static Regex ParseRegex(string pattern, string flags, string expression)
    {
        var options = RegexOptions.None;
        foreach (var flag in flags)
        {
            options |= flag switch
            {
                'i' => RegexOptions.IgnoreCase,
                'm' => RegexOptions.Multiline,
                's' => RegexOptions.Singleline,
                'U' => RegexOptions.None,
                _ => throw new GitLabExpressionException($"Unknown regex flag '{flag}' in '{expression}'")
            };
        }

        try
        {
            return new Regex(pattern, options | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new GitLabExpressionException($"Invalid regular expression '/{pattern}/{flags}' in '{expression}': {ex.Message}", ex);
        }
    }

    private enum TokenKind
    {
        LParen,
        RParen,
        And,
        Or,
        Equal,
        NotEqual,
        Match,
        NotMatch,
        Variable,
        String,
        Regex,
        Null,
        Word,
        End
    }

    private readonly record struct Token(TokenKind Kind, string Text, string Flags, int Position);

    private static class Tokenizer
    {
        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            var expectOperand = true;

            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                var start = i;
                switch (c)
                {
                    case '(':
                        tokens.Add(new Token(TokenKind.LParen, "(", string.Empty, start));
                        i++;
                        expectOperand = true;
                        continue;
                    case ')':
                        tokens.Add(new Token(TokenKind.RParen, ")", string.Empty, start));
                        i++;
                        expectOperand = false;
                        continue;
                    case '&' when Peek(text, i + 1) == '&':
                        tokens.Add(new Token(TokenKind.And, "&&", string.Empty, start));
                        i += 2;
                        expectOperand = true;
                        continue;
                    case '|' when Peek(text, i + 1) == '|':
                        tokens.Add(new Token(TokenKind.Or, "||", string.Empty, start));
                        i += 2;
                        expectOperand = true;
                        continue;
                    case '=' when Peek(text, i + 1) == '=':
                        tokens.Add(new Token(TokenKind.Equal, "==", string.Empty, start));
                        i += 2;
                        expectOperand = true;
                        continue;
                    case '!' when Peek(text, i + 1) == '=':
                        tokens.Add(new Token(TokenKind.NotEqual, "!=", string.Empty, start));
                        i += 2;
                        expectOperand = true;
                        continue;
                    case '=' when Peek(text, i + 1) == '~':
                        tokens.Add(new Token(TokenKind.Match, "=~", string.Empty, start));
                        i += 2;
                        expectOperand = true;
                        continue;
                    case '!' when Peek(text, i + 1) == '~':
                        tokens.Add(new Token(TokenKind.NotMatch, "!~", string.Empty, start));
                        i += 2;
                        expectOperand = true;
                        continue;
                    case '$':
                        tokens.Add(ReadVariable(text, ref i));
                        expectOperand = false;
                        continue;
                    case '"':
                    case '\'':
                        tokens.Add(ReadString(text, ref i));
                        expectOperand = false;
                        continue;
                    case '/' when expectOperand:
                        tokens.Add(ReadRegex(text, ref i));
                        expectOperand = false;
                        continue;
                }

                if (IsWordChar(c))
                {
                    var end = i;
                    while (end < text.Length && IsWordChar(text[end]))
                    {
                        end++;
                    }

                    var word = text[i..end];
                    tokens.Add(word == "null"
                        ? new Token(TokenKind.Null, word, string.Empty, start)
                        : new Token(TokenKind.Word, word, string.Empty, start));
                    i = end;
                    expectOperand = false;
                    continue;
                }

                throw new GitLabExpressionException($"Unexpected character '{c}' at position {start} in '{text}'", start);
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, string.Empty, text.Length));
            return tokens;
        }

        private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '@';

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static Token ReadVariable(string text, ref int i)
        {
            var start = i;
            i++; // $
            if (Peek(text, i) == '{')
            {
                var close = text.IndexOf('}', i + 1);
                if (close < 0)
                {
                    throw new GitLabExpressionException($"Unterminated variable reference at position {start} in '{text}'", start);
                }

                var braced = text[(i + 1)..close];
                if (braced.Length == 0 || !braced.All(IsNameChar))
                {
                    throw new GitLabExpressionException($"Invalid variable name '${{{braced}}}' at position {start} in '{text}'", start);
                }

                i = close + 1;
                return new Token(TokenKind.Variable, braced, string.Empty, start);
            }

            var end = i;
            while (end < text.Length && IsNameChar(text[end]))
            {
                end++;
            }

            if (end == i)
            {
                throw new GitLabExpressionException($"Expected a variable name after '$' at position {start} in '{text}'", start);
            }

            var name = text[i..end];
            i = end;
            return new Token(TokenKind.Variable, name, string.Empty, start);
        }

        private static Token ReadString(string text, ref int i)
        {
            var start = i;
            var quote = text[i];
            i++;
            var sb = new StringBuilder();
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length && (text[i + 1] == quote || text[i + 1] == '\\'))
                {
                    sb.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    i++;
                    return new Token(TokenKind.String, sb.ToString(), string.Empty, start);
                }

                sb.Append(c);
                i++;
            }

            throw new GitLabExpressionException($"Unterminated string starting at position {start} in '{text}'", start);
        }

        private static Token ReadRegex(string text, ref int i)
        {
            var start = i;
            i++; // opening slash
            var sb = new StringBuilder();
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    if (text[i + 1] == '/')
                    {
                        sb.Append('/');
                    }
                    else
                    {
                        sb.Append(c).Append(text[i + 1]);
                    }

                    i += 2;
                    continue;
                }

                if (c == '/')
                {
                    i++;
                    var flagsStart = i;
                    while (i < text.Length && char.IsLetter(text[i]))
                    {
                        i++;
                    }

                    return new Token(TokenKind.Regex, sb.ToString(), text[flagsStart..i], start);
                }

                if (c == '\n')
                {
                    break;
                }

                sb.Append(c);
                i++;
            }

            throw new GitLabExpressionException($"Unterminated regular expression starting at position {start} in '{text}'", start);
        }
    }

    private abstract class Node
    {
        public abstract object? Evaluate(Func<string, string?> resolve);
    }

    private sealed class VariableNode : Node
    {
        private readonly string _name;

        public VariableNode(string name) => _name = name;

        public override object? Evaluate(Func<string, string?> resolve) => resolve(_name);
    }

    private sealed class LiteralNode : Node
    {
        private readonly string? _value;

        public LiteralNode(string? value) => _value = value;

        public override object? Evaluate(Func<string, string?> resolve) => _value;
    }

    private sealed class RegexLiteralNode : Node
    {
        public RegexLiteralNode(Regex regex) => Regex = regex;

        public Regex Regex { get; }

        public override object? Evaluate(Func<string, string?> resolve) => "/" + Regex + "/";
    }

    private sealed class EqualityNode : Node
    {
        private readonly Node _left;
        private readonly Node _right;
        private readonly bool _negate;

        public EqualityNode(Node left, Node right, bool negate)
        {
            _left = left;
            _right = right;
            _negate = negate;
        }

        public override object? Evaluate(Func<string, string?> resolve)
        {
            var left = AsString(_left.Evaluate(resolve));
            var right = AsString(_right.Evaluate(resolve));
            var equal = left is null ? right is null : right is not null && string.Equals(left, right, StringComparison.Ordinal);
            return _negate ? !equal : equal;
        }
    }

    private sealed class MatchNode : Node
    {
        private readonly Node _left;
        private readonly Node _pattern;
        private readonly bool _negate;
        private readonly string _expression;

        public MatchNode(Node left, Node pattern, bool negate, string expression)
        {
            _left = left;
            _pattern = pattern;
            _negate = negate;
            _expression = expression;
        }

        public override object? Evaluate(Func<string, string?> resolve)
        {
            var subject = AsString(_left.Evaluate(resolve));
            Regex regex;
            if (_pattern is RegexLiteralNode literal)
            {
                regex = literal.Regex;
            }
            else
            {
                // `$VAR =~ $PATTERN`: the pattern variable holds `/pattern/flags` (a bare pattern is accepted too)
                var text = AsString(_pattern.Evaluate(resolve));
                if (text is null)
                {
                    return _negate;
                }

                var slash = text.Length > 1 && text[0] == '/' ? text.LastIndexOf('/') : -1;
                regex = slash > 0
                    ? ParseRegex(text[1..slash], text[(slash + 1)..], _expression)
                    : ParseRegex(text, string.Empty, _expression);
            }

            if (subject is null)
            {
                return _negate;
            }

            bool matches;
            try
            {
                matches = regex.IsMatch(subject);
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw new GitLabExpressionException($"Regular expression '{regex}' timed out in '{_expression}'", ex);
            }

            return _negate ? !matches : matches;
        }
    }

    private sealed class AndNode : Node
    {
        private readonly Node _left;
        private readonly Node _right;

        public AndNode(Node left, Node right)
        {
            _left = left;
            _right = right;
        }

        public override object? Evaluate(Func<string, string?> resolve) =>
            IsTruthy(_left.Evaluate(resolve)) && IsTruthy(_right.Evaluate(resolve));
    }

    private sealed class OrNode : Node
    {
        private readonly Node _left;
        private readonly Node _right;

        public OrNode(Node left, Node right)
        {
            _left = left;
            _right = right;
        }

        public override object? Evaluate(Func<string, string?> resolve) =>
            IsTruthy(_left.Evaluate(resolve)) || IsTruthy(_right.Evaluate(resolve));
    }

    private static string? AsString(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly string _expression;
        private int _index;

        public Parser(List<Token> tokens, string expression)
        {
            _tokens = tokens;
            _expression = expression;
        }

        private Token Current => _tokens[_index];

        public Node ParseExpression()
        {
            var node = ParseOr();
            if (Current.Kind != TokenKind.End)
            {
                throw Unexpected();
            }

            return node;
        }

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (Current.Kind == TokenKind.Or)
            {
                _index++;
                left = new OrNode(left, ParseAnd());
            }

            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseUnary();
            while (Current.Kind == TokenKind.And)
            {
                _index++;
                left = new AndNode(left, ParseUnary());
            }

            return left;
        }

        private Node ParseUnary()
        {
            if (Current.Kind == TokenKind.LParen)
            {
                _index++;
                var inner = ParseOr();
                if (Current.Kind != TokenKind.RParen)
                {
                    throw new GitLabExpressionException($"Expected ')' at position {Current.Position} in '{_expression}'", Current.Position);
                }

                _index++;
                return inner;
            }

            return ParseComparison();
        }

        private Node ParseComparison()
        {
            var left = ParseOperand();
            switch (Current.Kind)
            {
                case TokenKind.Equal:
                case TokenKind.NotEqual:
                {
                    var negate = Current.Kind == TokenKind.NotEqual;
                    _index++;
                    var right = ParseOperand();
                    return new EqualityNode(left, right, negate);
                }

                case TokenKind.Match:
                case TokenKind.NotMatch:
                {
                    var negate = Current.Kind == TokenKind.NotMatch;
                    _index++;
                    Node pattern;
                    if (Current.Kind == TokenKind.Regex)
                    {
                        pattern = new RegexLiteralNode(ParseRegex(Current.Text, Current.Flags, _expression));
                        _index++;
                    }
                    else
                    {
                        pattern = ParseOperand();
                    }

                    return new MatchNode(left, pattern, negate, _expression);
                }

                default:
                    return left;
            }
        }

        private Node ParseOperand()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Variable:
                    _index++;
                    return new VariableNode(token.Text);
                case TokenKind.String:
                case TokenKind.Word:
                    _index++;
                    return new LiteralNode(token.Text);
                case TokenKind.Null:
                    _index++;
                    return new LiteralNode(null);
                case TokenKind.Regex:
                    _index++;
                    return new RegexLiteralNode(ParseRegex(token.Text, token.Flags, _expression));
                default:
                    throw Unexpected();
            }
        }

        private GitLabExpressionException Unexpected()
        {
            var token = Current;
            var what = token.Kind == TokenKind.End ? "end of expression" : $"'{token.Text}'";
            return new GitLabExpressionException($"Unexpected {what} at position {token.Position} in '{_expression}'", token.Position);
        }
    }
}
