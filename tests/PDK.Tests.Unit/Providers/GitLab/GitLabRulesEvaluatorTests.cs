using FluentAssertions;
using PDK.Providers.GitLab;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitLab;

public class GitLabRulesEvaluatorTests
{
    private static readonly Dictionary<string, string> Variables = new(StringComparer.Ordinal)
    {
        ["CI_COMMIT_BRANCH"] = "main",
        ["CI_DEFAULT_BRANCH"] = "main",
        ["CI_PIPELINE_SOURCE"] = "push",
        ["CI_COMMIT_TAG"] = "v1.2.3",
        ["EMPTY"] = "",
        ["DEPLOY"] = "true",
        ["PATTERN"] = "/^v\\d+/",
        ["QUOTED"] = "say \"hi\""
    };

    private static bool Eval(string expression) => GitLabRulesEvaluator.Evaluate(expression, Variables);

    [Theory]
    [InlineData("$CI_COMMIT_BRANCH", true)]
    [InlineData("$EMPTY", false)]
    [InlineData("$UNDEFINED", false)]
    [InlineData("${CI_COMMIT_BRANCH}", true)]
    public void Variable_IsTruthy_WhenDefinedAndNotEmpty(string expression, bool expected)
    {
        Eval(expression).Should().Be(expected);
    }

    [Theory]
    [InlineData("$CI_COMMIT_BRANCH == \"main\"", true)]
    [InlineData("$CI_COMMIT_BRANCH == 'main'", true)]
    [InlineData("$CI_COMMIT_BRANCH == \"develop\"", false)]
    [InlineData("$CI_COMMIT_BRANCH != \"develop\"", true)]
    [InlineData("$CI_COMMIT_BRANCH != \"main\"", false)]
    [InlineData("$CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH", true)]
    [InlineData("\"main\" == $CI_COMMIT_BRANCH", true)]
    [InlineData("$CI_COMMIT_BRANCH == main", true)]
    public void Equality_ComparesStrings(string expression, bool expected)
    {
        Eval(expression).Should().Be(expected);
    }

    [Theory]
    [InlineData("$UNDEFINED == null", true)]
    [InlineData("$UNDEFINED != null", false)]
    [InlineData("$EMPTY == null", false)]
    [InlineData("$EMPTY == \"\"", true)]
    [InlineData("$UNDEFINED == \"\"", false)]
    [InlineData("$CI_COMMIT_BRANCH != null", true)]
    public void Null_MatchesOnlyUndefinedVariables(string expression, bool expected)
    {
        Eval(expression).Should().Be(expected);
    }

    [Theory]
    [InlineData("$CI_COMMIT_BRANCH =~ /^ma/", true)]
    [InlineData("$CI_COMMIT_BRANCH =~ /^MA/", false)]
    [InlineData("$CI_COMMIT_BRANCH =~ /^MA/i", true)]
    [InlineData("$CI_COMMIT_BRANCH !~ /^ma/", false)]
    [InlineData("$CI_COMMIT_BRANCH !~ /release/", true)]
    [InlineData("$CI_COMMIT_TAG =~ /^v\\d+\\.\\d+\\.\\d+$/", true)]
    [InlineData("$CI_COMMIT_TAG =~ $PATTERN", true)]
    [InlineData("$CI_COMMIT_BRANCH =~ $PATTERN", false)]
    [InlineData("$UNDEFINED =~ /.*/", false)]
    [InlineData("$UNDEFINED !~ /.*/", true)]
    [InlineData("$CI_COMMIT_BRANCH =~ /ma\\/in/", false)]
    public void RegexMatch_SupportsFlagsAndPatternVariables(string expression, bool expected)
    {
        Eval(expression).Should().Be(expected);
    }

    [Fact]
    public void RegexMatch_EscapedSlash_MatchesLiteralSlash()
    {
        var variables = new Dictionary<string, string> { ["REF"] = "feature/login" };

        GitLabRulesEvaluator.Evaluate("$REF =~ /^feature\\/.+$/", variables).Should().BeTrue();
    }

    [Theory]
    [InlineData("$CI_COMMIT_BRANCH == \"main\" && $CI_PIPELINE_SOURCE == \"push\"", true)]
    [InlineData("$CI_COMMIT_BRANCH == \"main\" && $CI_PIPELINE_SOURCE == \"schedule\"", false)]
    [InlineData("$CI_COMMIT_BRANCH == \"other\" || $CI_PIPELINE_SOURCE == \"push\"", true)]
    [InlineData("$CI_COMMIT_BRANCH == \"other\" || $CI_PIPELINE_SOURCE == \"schedule\"", false)]
    [InlineData("$UNDEFINED || $DEPLOY", true)]
    [InlineData("$UNDEFINED && $DEPLOY", false)]
    public void BooleanOperators_Combine(string expression, bool expected)
    {
        Eval(expression).Should().Be(expected);
    }

    [Fact]
    public void And_BindsTighterThan_Or()
    {
        // true || (false && false) => true; (true || false) && false would be false
        Eval("$DEPLOY || $EMPTY && $UNDEFINED").Should().BeTrue();
        Eval("$EMPTY && $UNDEFINED || $DEPLOY").Should().BeTrue();
        Eval("$EMPTY || $UNDEFINED && $DEPLOY").Should().BeFalse();
    }

    [Fact]
    public void Parentheses_OverridePrecedence()
    {
        Eval("($DEPLOY || $EMPTY) && $UNDEFINED").Should().BeFalse();
        Eval("$DEPLOY || ($EMPTY && $UNDEFINED)").Should().BeTrue();
        Eval("(($CI_COMMIT_BRANCH == \"main\"))").Should().BeTrue();
    }

    [Theory]
    [InlineData("$QUOTED == \"say \\\"hi\\\"\"", true)]
    [InlineData("$QUOTED == 'say \"hi\"'", true)]
    [InlineData("$CI_COMMIT_BRANCH == \"ma in\"", false)]
    public void Strings_SupportQuotesAndEscapes(string expression, bool expected)
    {
        Eval(expression).Should().Be(expected);
    }

    [Fact]
    public void EmptyExpression_IsTrue()
    {
        Eval(string.Empty).Should().BeTrue();
        Eval("   ").Should().BeTrue();
    }

    [Fact]
    public void Whitespace_IsInsignificant()
    {
        Eval("$CI_COMMIT_BRANCH==\"main\"&&$DEPLOY").Should().BeTrue();
        Eval("  $CI_COMMIT_BRANCH   ==   \"main\"  ").Should().BeTrue();
    }

    [Theory]
    [InlineData("$CI_COMMIT_BRANCH ==")]
    [InlineData("== \"main\"")]
    [InlineData("$CI_COMMIT_BRANCH == \"main")]
    [InlineData("($CI_COMMIT_BRANCH == \"main\"")]
    [InlineData("$CI_COMMIT_BRANCH =~ /unterminated")]
    [InlineData("$CI_COMMIT_BRANCH =~ /ok/z")]
    [InlineData("$CI_COMMIT_BRANCH & $DEPLOY")]
    [InlineData("$")]
    [InlineData("${}")]
    [InlineData("$A $B")]
    public void InvalidExpressions_Throw(string expression)
    {
        var act = () => Eval(expression);

        act.Should().Throw<GitLabExpressionException>();
    }

    [Fact]
    public void InvalidRegex_Throws()
    {
        var act = () => Eval("$CI_COMMIT_BRANCH =~ /(unclosed/");

        act.Should().Throw<GitLabExpressionException>().WithMessage("*Invalid regular expression*");
    }

    [Fact]
    public void ResolverConstructor_ConsultsCallback()
    {
        var evaluator = new GitLabRulesEvaluator(name => name == "X" ? "1" : null);

        evaluator.Evaluate("$X == \"1\"").Should().BeTrue();
        evaluator.Evaluate("$Y == null").Should().BeTrue();
    }

    [Fact]
    public void VariableNames_AreCaseSensitive()
    {
        Eval("$ci_commit_branch == \"main\"").Should().BeFalse();
        Eval("$ci_commit_branch == null").Should().BeTrue();
    }
}
