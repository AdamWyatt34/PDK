namespace PDK.Tests.Unit.Variables;

using FluentAssertions;
using Moq;
using PDK.Core.ErrorHandling;
using PDK.Core.Variables;
using Xunit;

/// <summary>
/// Unit tests for VariableExpander.
/// </summary>
public class VariableExpanderTests
{
    private readonly Mock<IVariableResolver> _mockResolver;
    private readonly VariableExpander _expander;

    public VariableExpanderTests()
    {
        _mockResolver = new Mock<IVariableResolver>();
        _expander = new VariableExpander();
    }

    #region Simple Expansion Tests

    [Fact]
    public void Expand_ExpandsSimpleVariable()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("MY_VAR")).Returns("my_value");

        // Act
        var result = _expander.Expand("Hello ${MY_VAR}!", _mockResolver.Object);

        // Assert
        result.Should().Be("Hello my_value!");
    }

    [Fact]
    public void Expand_ExpandsMultipleVariables()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("FIRST")).Returns("one");
        _mockResolver.Setup(r => r.Resolve("SECOND")).Returns("two");

        // Act
        var result = _expander.Expand("${FIRST} and ${SECOND}", _mockResolver.Object);

        // Assert
        result.Should().Be("one and two");
    }

    [Fact]
    public void Expand_ReturnsEmptyString_ForUndefinedVariable()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("UNDEFINED")).Returns((string?)null);

        // Act
        var result = _expander.Expand("Value: ${UNDEFINED}", _mockResolver.Object);

        // Assert
        result.Should().Be("Value: ");
    }

    [Fact]
    public void Expand_PreservesTextWithoutVariables()
    {
        // Act
        var result = _expander.Expand("No variables here", _mockResolver.Object);

        // Assert
        result.Should().Be("No variables here");
    }

    [Fact]
    public void Expand_HandlesEmptyInput()
    {
        // Act
        var result = _expander.Expand("", _mockResolver.Object);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_HandlesNullInput()
    {
        // Act
        var result = _expander.Expand(null!, _mockResolver.Object);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_ThrowsOnNullResolver()
    {
        // Act
        var act = () => _expander.Expand("${VAR}", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Default Value Tests

    [Fact]
    public void Expand_UsesDefaultValue_WhenVariableUndefined()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("UNDEFINED")).Returns((string?)null);

        // Act
        var result = _expander.Expand("${UNDEFINED:-default}", _mockResolver.Object);

        // Assert
        result.Should().Be("default");
    }

    [Fact]
    public void Expand_UsesDefaultValue_WhenVariableEmpty()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("EMPTY")).Returns("");

        // Act
        var result = _expander.Expand("${EMPTY:-default}", _mockResolver.Object);

        // Assert
        result.Should().Be("default");
    }

    [Fact]
    public void Expand_UsesActualValue_WhenSet()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("SET")).Returns("actual");

        // Act
        var result = _expander.Expand("${SET:-default}", _mockResolver.Object);

        // Assert
        result.Should().Be("actual");
    }

    [Fact]
    public void Expand_DefaultValue_CanBeEmpty()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("UNDEFINED")).Returns((string?)null);

        // Act
        var result = _expander.Expand("${UNDEFINED:-}", _mockResolver.Object);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_DefaultValue_CanContainSpecialChars()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("UNDEFINED")).Returns((string?)null);

        // Act
        var result = _expander.Expand("${UNDEFINED:-/path/to/file}", _mockResolver.Object);

        // Assert
        result.Should().Be("/path/to/file");
    }

    #endregion

    #region Required Variable Tests

    [Fact]
    public void Expand_ThrowsVariableException_WhenRequiredVariableUndefined()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("REQUIRED")).Returns((string?)null);

        // Act
        var act = () => _expander.Expand("${REQUIRED:?Variable is required}", _mockResolver.Object);

        // Assert
        var ex = act.Should().Throw<VariableException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.VariableRequired);
        ex.VariableName.Should().Be("REQUIRED");
        ex.Message.Should().Contain("Variable is required");
    }

    [Fact]
    public void Expand_ThrowsVariableException_WhenRequiredVariableEmpty()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("REQUIRED")).Returns("");

        // Act
        var act = () => _expander.Expand("${REQUIRED:?Must be set}", _mockResolver.Object);

        // Assert
        var ex = act.Should().Throw<VariableException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.VariableRequired);
    }

    [Fact]
    public void Expand_UsesActualValue_WhenRequiredVariableSet()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("REQUIRED")).Returns("value");

        // Act
        var result = _expander.Expand("${REQUIRED:?error}", _mockResolver.Object);

        // Assert
        result.Should().Be("value");
    }

    [Fact]
    public void Expand_RequiredVariable_DefaultErrorMessage()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("VAR")).Returns((string?)null);

        // Act
        var act = () => _expander.Expand("${VAR:?}", _mockResolver.Object);

        // Assert
        var ex = act.Should().Throw<VariableException>().Which;
        ex.Message.Should().Contain("VAR");
    }

    #endregion

    #region Escaped Variable Tests

    [Fact]
    public void Expand_EscapedVariable_NotExpanded()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("VAR")).Returns("value");

        // Act
        var result = _expander.Expand("\\${VAR}", _mockResolver.Object);

        // Assert
        result.Should().Be("${VAR}");
    }

    [Fact]
    public void Expand_MixedEscapedAndNormal()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("EXPAND")).Returns("expanded");
        _mockResolver.Setup(r => r.Resolve("KEEP")).Returns("should_not_see");

        // Act
        var result = _expander.Expand("${EXPAND} and \\${KEEP}", _mockResolver.Object);

        // Assert
        result.Should().Be("expanded and ${KEEP}");
    }

    #endregion

    #region Nested/Recursive Expansion Tests

    [Fact]
    public void Expand_ExpandsNestedVariables()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("OUTER")).Returns("${INNER}");
        _mockResolver.Setup(r => r.Resolve("INNER")).Returns("nested_value");

        // Act
        var result = _expander.Expand("${OUTER}", _mockResolver.Object);

        // Assert
        result.Should().Be("nested_value");
    }

    [Fact]
    public void Expand_ThrowsOnCircularReference()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("A")).Returns("${B}");
        _mockResolver.Setup(r => r.Resolve("B")).Returns("${A}");

        // Act
        var act = () => _expander.Expand("${A}", _mockResolver.Object);

        // Assert
        var ex = act.Should().Throw<VariableException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.VariableCircularReference);
    }

    [Fact]
    public void Expand_ThrowsOnSelfReference()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("SELF")).Returns("${SELF}");

        // Act
        var act = () => _expander.Expand("${SELF}", _mockResolver.Object);

        // Assert
        var ex = act.Should().Throw<VariableException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.VariableCircularReference);
    }

    [Fact]
    public void Expand_ThrowsOnRecursionLimit()
    {
        // Arrange
        var expander = new VariableExpander(5); // Low limit for testing

        // Create a chain that exceeds the limit
        for (int i = 1; i <= 10; i++)
        {
            var current = $"VAR{i}";
            var next = i < 10 ? $"${{VAR{i + 1}}}" : "final";
            _mockResolver.Setup(r => r.Resolve(current)).Returns(next);
        }

        // Act
        var act = () => expander.Expand("${VAR1}", _mockResolver.Object);

        // Assert
        var ex = act.Should().Throw<VariableException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.VariableRecursionLimit);
    }

    [Fact]
    public void Expand_AllowsNonCircularReuse()
    {
        // Arrange - Same variable used multiple times, but not circular
        _mockResolver.Setup(r => r.Resolve("COMMON")).Returns("common_value");
        _mockResolver.Setup(r => r.Resolve("FIRST")).Returns("${COMMON}");
        _mockResolver.Setup(r => r.Resolve("SECOND")).Returns("${COMMON}");

        // Act
        var result = _expander.Expand("${FIRST} and ${SECOND}", _mockResolver.Object);

        // Assert
        result.Should().Be("common_value and common_value");
    }

    #endregion

    #region ContainsVariables Tests

    [Theory]
    [InlineData("${VAR}", true)]
    [InlineData("${VAR:-default}", true)]
    [InlineData("${VAR:?error}", true)]
    [InlineData("Text ${VAR} more", true)]
    [InlineData("No variables", false)]
    [InlineData("\\${ESCAPED}", false)]
    [InlineData("", false)]
    public void ContainsVariables_DetectsVariables(string input, bool expected)
    {
        // Act
        var result = _expander.ContainsVariables(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ContainsVariables_ReturnsFalse_ForNull()
    {
        // Act
        var result = _expander.ContainsVariables(null!);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ExtractVariableNames Tests

    [Fact]
    public void ExtractVariableNames_ExtractsAllNames()
    {
        // Act
        var names = _expander.ExtractVariableNames("${VAR1} and ${VAR2}").ToList();

        // Assert
        names.Should().Contain("VAR1");
        names.Should().Contain("VAR2");
        names.Should().HaveCount(2);
    }

    [Fact]
    public void ExtractVariableNames_DeduplicatesNames()
    {
        // Act
        var names = _expander.ExtractVariableNames("${VAR} and ${VAR}").ToList();

        // Assert
        names.Should().ContainSingle().Which.Should().Be("VAR");
    }

    [Fact]
    public void ExtractVariableNames_IgnoresEscaped()
    {
        // Act
        var names = _expander.ExtractVariableNames("${VAR1} and \\${ESCAPED}").ToList();

        // Assert
        names.Should().ContainSingle().Which.Should().Be("VAR1");
    }

    [Fact]
    public void ExtractVariableNames_ReturnsEmpty_ForNoVariables()
    {
        // Act
        var names = _expander.ExtractVariableNames("No variables").ToList();

        // Assert
        names.Should().BeEmpty();
    }

    [Fact]
    public void ExtractVariableNames_ReturnsEmpty_ForNull()
    {
        // Act
        var names = _expander.ExtractVariableNames(null!).ToList();

        // Assert
        names.Should().BeEmpty();
    }

    #endregion

    #region ExpandDictionary Tests

    [Fact]
    public void ExpandDictionary_ExpandsAllValues()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("VAR")).Returns("expanded");
        var input = new Dictionary<string, string>
        {
            ["key1"] = "${VAR}",
            ["key2"] = "static"
        };

        // Act
        var result = _expander.ExpandDictionary(input, _mockResolver.Object);

        // Assert
        result["key1"].Should().Be("expanded");
        result["key2"].Should().Be("static");
    }

    [Fact]
    public void ExpandDictionary_ThrowsOnNullInput()
    {
        // Act
        var act = () => _expander.ExpandDictionary(null!, _mockResolver.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExpandDictionary_ThrowsOnNullResolver()
    {
        // Arrange
        var input = new Dictionary<string, string> { ["key"] = "value" };

        // Act
        var act = () => _expander.ExpandDictionary(input, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region MaxRecursionDepth Tests

    [Fact]
    public void MaxRecursionDepth_DefaultsTo10()
    {
        // Arrange
        var expander = new VariableExpander();

        // Assert
        expander.MaxRecursionDepth.Should().Be(10);
    }

    [Fact]
    public void MaxRecursionDepth_CanBeCustomized()
    {
        // Arrange
        var expander = new VariableExpander(20);

        // Assert
        expander.MaxRecursionDepth.Should().Be(20);
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidRecursionDepth()
    {
        // Act
        var act = () => new VariableExpander(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Variable Name Pattern Tests

    [Theory]
    [InlineData("${A}")]
    [InlineData("${a}")]
    [InlineData("${_VAR}")]
    [InlineData("${VAR_NAME}")]
    [InlineData("${VAR123}")]
    [InlineData("${MY_VAR_123}")]
    public void Expand_ValidVariableNames(string input)
    {
        // Arrange
        var varName = input[2..^1]; // Extract name from ${NAME}
        _mockResolver.Setup(r => r.Resolve(varName)).Returns("value");

        // Act
        var result = _expander.Expand(input, _mockResolver.Object);

        // Assert
        result.Should().Be("value");
    }

    [Fact]
    public void Expand_InvalidVariableName_TreatedAsLiteral()
    {
        // Arrange - ${123} starts with a digit, invalid
        // This should not be matched by the regex

        // Act
        var result = _expander.Expand("${123}", _mockResolver.Object);

        // Assert
        result.Should().Be("${123}"); // Left as literal
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Expand_VariableAtStartAndEnd()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("START")).Returns("begin");
        _mockResolver.Setup(r => r.Resolve("END")).Returns("finish");

        // Act
        var result = _expander.Expand("${START}middle${END}", _mockResolver.Object);

        // Assert
        result.Should().Be("beginmiddlefinish");
    }

    [Fact]
    public void Expand_AdjacentVariables()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("A")).Returns("one");
        _mockResolver.Setup(r => r.Resolve("B")).Returns("two");

        // Act
        var result = _expander.Expand("${A}${B}", _mockResolver.Object);

        // Assert
        result.Should().Be("onetwo");
    }

    [Fact]
    public void Expand_VariableOnly()
    {
        // Arrange
        _mockResolver.Setup(r => r.Resolve("VAR")).Returns("value");

        // Act
        var result = _expander.Expand("${VAR}", _mockResolver.Object);

        // Assert
        result.Should().Be("value");
    }

    #endregion
}
