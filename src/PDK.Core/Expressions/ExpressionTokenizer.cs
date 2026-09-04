using System.Globalization;
using System.Text;

namespace PDK.Core.Expressions;

/// <summary>Token kinds produced by <see cref="ExpressionTokenizer"/>.</summary>
public enum TokenKind
{
    /// <summary>An identifier or keyword (true/false/null are identifiers resolved by the parser).</summary>
    Identifier,
    /// <summary>A numeric literal.</summary>
    Number,
    /// <summary>A single-quoted string literal (already unescaped).</summary>
    StringLiteral,
    /// <summary>Punctuation or operator: ( ) [ ] . , * ! == != &lt; &lt;= &gt; &gt;= &amp;&amp; ||</summary>
    Punct,
    /// <summary>End of input.</summary>
    End
}

/// <summary>A token with its position in the source expression.</summary>
/// <param name="Kind">Token kind.</param>
/// <param name="Text">Token text (unescaped for strings).</param>
/// <param name="Position">Zero-based position in the source.</param>
public readonly record struct Token(TokenKind Kind, string Text, int Position);

/// <summary>
/// Splits a pipeline expression into tokens. Shared by the GitHub and Azure dialects.
/// </summary>
public static class ExpressionTokenizer
{
    private static readonly string[] TwoCharOps = ["==", "!=", "<=", ">=", "&&", "||"];

    /// <summary>Tokenizes <paramref name="text"/>.</summary>
    /// <exception cref="ExpressionException">Thrown on an unterminated string or an unexpected character.</exception>
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '\'')
            {
                var sb = new StringBuilder();
                var start = i;
                i++;
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == '\'')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i += 2;
                            continue;
                        }

                        closed = true;
                        i++;
                        break;
                    }

                    sb.Append(text[i]);
                    i++;
                }

                if (!closed)
                {
                    throw new ExpressionException(text, $"unterminated string literal at position {start}");
                }

                tokens.Add(new Token(TokenKind.StringLiteral, sb.ToString(), start));
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && IsUnaryMinusPosition(tokens)))
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.' || text[i] == '-' || text[i] == '+'))
                {
                    // allow 1e5, 0x1F, 1.5; stop at a second dot followed by a non-digit ("1.foo" is not a number)
                    if (text[i] == '.' && (i + 1 >= text.Length || !char.IsDigit(text[i + 1])))
                    {
                        break;
                    }
                    if ((text[i] == '-' || text[i] == '+') && !(text[i - 1] == 'e' || text[i - 1] == 'E'))
                    {
                        break;
                    }
                    i++;
                }

                var numberText = text[start..i];
                if (!TryParseNumber(numberText, out _))
                {
                    throw new ExpressionException(text, $"invalid number '{numberText}' at position {start}");
                }

                tokens.Add(new Token(TokenKind.Number, numberText, start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '-'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Identifier, text[start..i], start));
                continue;
            }

            if (i + 1 < text.Length)
            {
                var two = text.Substring(i, 2);
                if (Array.IndexOf(TwoCharOps, two) >= 0)
                {
                    tokens.Add(new Token(TokenKind.Punct, two, i));
                    i += 2;
                    continue;
                }
            }

            if ("()[].,*!<>".Contains(c))
            {
                tokens.Add(new Token(TokenKind.Punct, c.ToString(), i));
                i++;
                continue;
            }

            throw new ExpressionException(text, $"unexpected character '{c}' at position {i}");
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));
        return tokens;
    }

    private static bool IsUnaryMinusPosition(List<Token> tokens)
    {
        if (tokens.Count == 0)
        {
            return true;
        }

        var last = tokens[^1];
        return last.Kind == TokenKind.Punct && last.Text is not ")" and not "]";
    }

    /// <summary>Parses a numeric literal (decimal, hex 0x.., exponent).</summary>
    public static bool TryParseNumber(string text, out double value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
        {
            var negative = text[0] == '-';
            var hex = text[(negative ? 3 : 2)..];
            if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var l))
            {
                value = negative ? -l : l;
                return true;
            }

            value = 0;
            return false;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
