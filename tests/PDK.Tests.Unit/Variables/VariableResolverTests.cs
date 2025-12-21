namespace PDK.Tests.Unit.Variables;

using FluentAssertions;
using Moq;
using PDK.Core.Configuration;
using PDK.Core.Variables;
using Xunit;

/// <summary>
/// Unit tests for VariableResolver.
/// </summary>
public class VariableResolverTests
{
    #region Resolve Tests

    [Fact]
    public void Resolve_ReturnsValue_ForSetVariable()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("MY_VAR", "my_value", VariableSource.Configuration);

        // Act
        var result = resolver.Resolve("MY_VAR");

        // Assert
        result.Should().Be("my_value");
    }

    [Fact]
    public void Resolve_ReturnsNull_ForUnsetVariable()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.Resolve("UNSET_VAR");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_ReturnsBuiltInVariable()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.Resolve("PDK_VERSION");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Resolve_WithDefault_ReturnsDefault_WhenNotFound()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.Resolve("UNSET_VAR", "default_value");

        // Assert
        result.Should().Be("default_value");
    }

    [Fact]
    public void Resolve_WithDefault_ReturnsValue_WhenFound()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("MY_VAR", "actual_value", VariableSource.Configuration);

        // Act
        var result = resolver.Resolve("MY_VAR", "default_value");

        // Assert
        result.Should().Be("actual_value");
    }

    [Fact]
    public void Resolve_ReturnsNull_ForEmptyName()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.Resolve("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_ReturnsNull_ForNullName()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.Resolve(null!);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Precedence Tests

    [Fact]
    public void SetVariable_CliOverridesEnvironment()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("VAR", "env_value", VariableSource.Environment);
        resolver.SetVariable("VAR", "cli_value", VariableSource.CliArgument);

        // Act
        var result = resolver.Resolve("VAR");

        // Assert
        result.Should().Be("cli_value");
    }

    [Fact]
    public void SetVariable_EnvironmentOverridesConfiguration()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("VAR", "config_value", VariableSource.Configuration);
        resolver.SetVariable("VAR", "env_value", VariableSource.Environment);

        // Act
        var result = resolver.Resolve("VAR");

        // Assert
        result.Should().Be("env_value");
    }

    [Fact]
    public void SetVariable_ConfigurationOverridesBuiltIn()
    {
        // Arrange
        var mockBuiltIn = new Mock<IBuiltInVariables>();
        mockBuiltIn.Setup(b => b.GetValue("CUSTOM")).Returns("builtin_value");
        mockBuiltIn.Setup(b => b.IsBuiltIn("CUSTOM")).Returns(true);

        var resolver = new VariableResolver(mockBuiltIn.Object);
        resolver.SetVariable("CUSTOM", "config_value", VariableSource.Configuration);

        // Act
        var result = resolver.Resolve("CUSTOM");

        // Assert
        result.Should().Be("config_value");
    }

    [Fact]
    public void SetVariable_LowerPrecedenceDoesNotOverride()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("VAR", "cli_value", VariableSource.CliArgument);
        resolver.SetVariable("VAR", "env_value", VariableSource.Environment);

        // Act
        var result = resolver.Resolve("VAR");

        // Assert
        result.Should().Be("cli_value");
    }

    [Fact]
    public void SetVariable_SamePrecedenceUpdates()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("VAR", "first", VariableSource.Configuration);
        resolver.SetVariable("VAR", "second", VariableSource.Configuration);

        // Act
        var result = resolver.Resolve("VAR");

        // Assert
        result.Should().Be("second");
    }

    #endregion

    #region ContainsVariable Tests

    [Fact]
    public void ContainsVariable_ReturnsTrue_ForSetVariable()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("MY_VAR", "value", VariableSource.Configuration);

        // Act
        var result = resolver.ContainsVariable("MY_VAR");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsVariable_ReturnsTrue_ForBuiltInVariable()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.ContainsVariable("PDK_VERSION");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsVariable_ReturnsFalse_ForUnsetVariable()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.ContainsVariable("UNSET_VAR");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsVariable_ReturnsFalse_ForEmptyName()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.ContainsVariable("");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetSource Tests

    [Fact]
    public void GetSource_ReturnsCorrectSource()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("CLI_VAR", "value", VariableSource.CliArgument);
        resolver.SetVariable("ENV_VAR", "value", VariableSource.Environment);
        resolver.SetVariable("CONFIG_VAR", "value", VariableSource.Configuration);

        // Act & Assert
        resolver.GetSource("CLI_VAR").Should().Be(VariableSource.CliArgument);
        resolver.GetSource("ENV_VAR").Should().Be(VariableSource.Environment);
        resolver.GetSource("CONFIG_VAR").Should().Be(VariableSource.Configuration);
    }

    [Fact]
    public void GetSource_ReturnsBuiltIn_ForBuiltInVariables()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.GetSource("PDK_VERSION");

        // Assert
        result.Should().Be(VariableSource.BuiltIn);
    }

    [Fact]
    public void GetSource_ReturnsNull_ForUnsetVariable()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var result = resolver.GetSource("UNSET_VAR");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetAllVariables Tests

    [Fact]
    public void GetAllVariables_IncludesAllSources()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("CLI_VAR", "cli", VariableSource.CliArgument);
        resolver.SetVariable("ENV_VAR", "env", VariableSource.Environment);
        resolver.SetVariable("CONFIG_VAR", "config", VariableSource.Configuration);

        // Act
        var all = resolver.GetAllVariables();

        // Assert
        all.Should().ContainKey("CLI_VAR");
        all.Should().ContainKey("ENV_VAR");
        all.Should().ContainKey("CONFIG_VAR");
        all.Should().ContainKey("PDK_VERSION"); // Built-in
    }

    [Fact]
    public void GetAllVariables_AppliesPrecedence()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("VAR", "config", VariableSource.Configuration);
        resolver.SetVariable("VAR", "cli", VariableSource.CliArgument);

        // Act
        var all = resolver.GetAllVariables();

        // Assert
        all["VAR"].Should().Be("cli");
    }

    #endregion

    #region ClearSource Tests

    [Fact]
    public void ClearSource_RemovesVariablesFromSource()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("CLI_VAR1", "value1", VariableSource.CliArgument);
        resolver.SetVariable("CLI_VAR2", "value2", VariableSource.CliArgument);
        resolver.SetVariable("ENV_VAR", "value3", VariableSource.Environment);

        // Act
        resolver.ClearSource(VariableSource.CliArgument);

        // Assert
        resolver.ContainsVariable("CLI_VAR1").Should().BeFalse();
        resolver.ContainsVariable("CLI_VAR2").Should().BeFalse();
        resolver.ContainsVariable("ENV_VAR").Should().BeTrue();
    }

    [Fact]
    public void ClearSource_DoesNotAffectOtherSources()
    {
        // Arrange
        var resolver = new VariableResolver();
        resolver.SetVariable("VAR", "env", VariableSource.Environment);
        resolver.SetVariable("VAR", "cli", VariableSource.CliArgument);

        // Act
        resolver.ClearSource(VariableSource.CliArgument);

        // Assert - Environment value should still be there
        resolver.Resolve("VAR").Should().BeNull(); // Because we only had CLI set for this var
    }

    #endregion

    #region LoadFromConfiguration Tests

    [Fact]
    public void LoadFromConfiguration_LoadsVariables()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["VAR1"] = "value1",
                ["VAR2"] = "value2"
            }
        };
        var resolver = new VariableResolver();

        // Act
        resolver.LoadFromConfiguration(config);

        // Assert
        resolver.Resolve("VAR1").Should().Be("value1");
        resolver.Resolve("VAR2").Should().Be("value2");
        resolver.GetSource("VAR1").Should().Be(VariableSource.Configuration);
    }

    [Fact]
    public void LoadFromConfiguration_ThrowsOnNull()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var act = () => resolver.LoadFromConfiguration(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LoadFromConfiguration_HandlesNullVariables()
    {
        // Arrange
        var config = new PdkConfig { Variables = null! };
        var resolver = new VariableResolver();

        // Act - Should not throw
        resolver.LoadFromConfiguration(config);

        // Assert
        resolver.GetAllVariables().Should().NotBeNull();
    }

    #endregion

    #region LoadFromEnvironment Tests

    [Fact]
    public void LoadFromEnvironment_LoadsEnvironmentVariables()
    {
        // Arrange
        var resolver = new VariableResolver();
        var testVar = $"PDK_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(testVar, "test_value");

        try
        {
            // Act
            resolver.LoadFromEnvironment();

            // Assert
            resolver.Resolve(testVar).Should().Be("test_value");
            resolver.GetSource(testVar).Should().Be(VariableSource.Environment);
        }
        finally
        {
            Environment.SetEnvironmentVariable(testVar, null);
        }
    }

    [Fact]
    public void LoadFromEnvironment_StripsPdkVarPrefix()
    {
        // Arrange
        var resolver = new VariableResolver();
        var testVar = $"MY_VAR_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable($"PDK_VAR_{testVar}", "prefixed_value");

        try
        {
            // Act
            resolver.LoadFromEnvironment();

            // Assert
            resolver.Resolve(testVar).Should().Be("prefixed_value");
        }
        finally
        {
            Environment.SetEnvironmentVariable($"PDK_VAR_{testVar}", null);
        }
    }

    #endregion

    #region UpdateContext Tests

    [Fact]
    public void UpdateContext_UpdatesBuiltInVariables()
    {
        // Arrange
        var resolver = new VariableResolver();
        var context = new VariableContext
        {
            Workspace = "/test/workspace",
            JobName = "build"
        };

        // Act
        resolver.UpdateContext(context);

        // Assert
        resolver.Resolve("PDK_WORKSPACE").Should().Be("/test/workspace");
        resolver.Resolve("PDK_JOB").Should().Be("build");
    }

    #endregion

    #region SetVariable Tests

    [Fact]
    public void SetVariable_ThrowsOnEmptyName()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var act = () => resolver.SetVariable("", "value", VariableSource.Configuration);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetVariable_ThrowsOnNullValue()
    {
        // Arrange
        var resolver = new VariableResolver();

        // Act
        var act = () => resolver.SetVariable("VAR", null!, VariableSource.Configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsOnNullBuiltIn()
    {
        // Act
        var act = () => new VariableResolver(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task Resolver_IsThreadSafe()
    {
        // Arrange
        var resolver = new VariableResolver();
        var tasks = new List<Task>();

        // Act - Multiple concurrent writes and reads
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                resolver.SetVariable($"VAR_{index}", $"value_{index}", VariableSource.Configuration);
                var result = resolver.Resolve($"VAR_{index}");
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - All variables should be set
        for (int i = 0; i < 100; i++)
        {
            resolver.ContainsVariable($"VAR_{i}").Should().BeTrue();
        }
    }

    #endregion
}
