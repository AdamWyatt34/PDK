namespace PDK.Tests.Integration;

using FluentAssertions;
using PDK.Core.Configuration;
using PDK.Core.Variables;
using Xunit;

/// <summary>
/// Integration tests for the variable resolution system.
/// Tests end-to-end variable resolution, expansion, and precedence handling.
/// </summary>
public class VariableIntegrationTests
{
    #region End-to-End Resolution Tests

    [Fact]
    public void FullWorkflow_ResolveAndExpand()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Load configuration variables
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["BUILD_DIR"] = "build",
                ["OUTPUT_DIR"] = "${BUILD_DIR}/output"
            }
        };
        resolver.LoadFromConfiguration(config);

        // Set CLI override
        resolver.SetVariable("ENVIRONMENT", "production", VariableSource.CliArgument);

        // Act
        var buildDir = expander.Expand("${BUILD_DIR}", resolver);
        var outputDir = expander.Expand("${OUTPUT_DIR}", resolver);
        var env = expander.Expand("${ENVIRONMENT}", resolver);
        var combined = expander.Expand("Deploying to ${ENVIRONMENT}: ${OUTPUT_DIR}", resolver);

        // Assert
        buildDir.Should().Be("build");
        outputDir.Should().Be("build/output");
        env.Should().Be("production");
        combined.Should().Be("Deploying to production: build/output");
    }

    [Fact]
    public void Precedence_CliOverridesAll()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Set from multiple sources
        resolver.SetVariable("MY_VAR", "builtin", VariableSource.BuiltIn);
        resolver.SetVariable("MY_VAR", "config", VariableSource.Configuration);
        resolver.SetVariable("MY_VAR", "env", VariableSource.Environment);
        resolver.SetVariable("MY_VAR", "cli", VariableSource.CliArgument);

        // Act
        var result = expander.Expand("${MY_VAR}", resolver);

        // Assert
        result.Should().Be("cli");
    }

    [Fact]
    public void Precedence_EnvironmentOverridesConfig()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        resolver.SetVariable("DB_HOST", "localhost", VariableSource.Configuration);
        resolver.SetVariable("DB_HOST", "production-db.example.com", VariableSource.Environment);

        // Act
        var result = expander.Expand("postgres://${DB_HOST}/mydb", resolver);

        // Assert
        result.Should().Be("postgres://production-db.example.com/mydb");
    }

    #endregion

    #region Built-in Variable Tests

    [Fact]
    public void BuiltInVariables_AreAccessible()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act
        var version = expander.Expand("PDK Version: ${PDK_VERSION}", resolver);
        var pwd = expander.Expand("Working dir: ${PWD}", resolver);
        var home = expander.Expand("Home: ${HOME}", resolver);

        // Assert
        version.Should().StartWith("PDK Version:");
        version.Should().NotEndWith("${PDK_VERSION}");
        pwd.Should().Contain(Environment.CurrentDirectory);
        home.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BuiltInVariables_ContextUpdates()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Update context
        var context = new VariableContext
        {
            Workspace = "/my/workspace",
            Runner = "docker",
            JobName = "build",
            StepName = "compile"
        };
        resolver.UpdateContext(context);

        // Act
        var result = expander.Expand("Running ${PDK_JOB}/${PDK_STEP} in ${PDK_WORKSPACE}", resolver);

        // Assert
        result.Should().Be("Running build/compile in /my/workspace");
    }

    #endregion

    #region Configuration Integration Tests

    [Fact]
    public void Configuration_VariablesAreLoaded()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["NODE_VERSION"] = "18",
                ["DOTNET_VERSION"] = "8.0"
            }
        };

        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act
        resolver.LoadFromConfiguration(config);
        var nodeResult = expander.Expand("node:${NODE_VERSION}", resolver);
        var dotnetResult = expander.Expand("mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}", resolver);

        // Assert
        nodeResult.Should().Be("node:18");
        dotnetResult.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
    }

    [Fact]
    public void Configuration_NestedVariableExpansion()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["BASE_IMAGE"] = "node",
                ["VERSION"] = "18-alpine",
                ["FULL_IMAGE"] = "${BASE_IMAGE}:${VERSION}"
            }
        };

        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.LoadFromConfiguration(config);

        // Act
        var result = expander.Expand("docker pull ${FULL_IMAGE}", resolver);

        // Assert
        result.Should().Be("docker pull node:18-alpine");
    }

    #endregion

    #region Environment Variable Integration Tests

    [Fact]
    public void Environment_VariablesAreLoaded()
    {
        // Arrange
        var testVar = $"PDK_TEST_VAR_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(testVar, "test_value");

        try
        {
            var resolver = new VariableResolver();
            var expander = new VariableExpander();
            resolver.LoadFromEnvironment();

            // Act
            var result = expander.Expand($"${{{testVar}}}", resolver);

            // Assert
            result.Should().Be("test_value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(testVar, null);
        }
    }

    [Fact]
    public void Environment_PdkVarPrefix_IsStripped()
    {
        // Arrange
        var varName = $"MY_CUSTOM_VAR_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable($"PDK_VAR_{varName}", "prefixed_value");

        try
        {
            var resolver = new VariableResolver();
            var expander = new VariableExpander();
            resolver.LoadFromEnvironment();

            // Act - Access without prefix
            var result = expander.Expand($"${{{varName}}}", resolver);

            // Assert
            result.Should().Be("prefixed_value");
        }
        finally
        {
            Environment.SetEnvironmentVariable($"PDK_VAR_{varName}", null);
        }
    }

    #endregion

    #region Default Value Tests

    [Fact]
    public void DefaultValues_UsedWhenVariableUndefined()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act
        var result = expander.Expand("${UNDEFINED:-default_value}", resolver);

        // Assert
        result.Should().Be("default_value");
    }

    [Fact]
    public void DefaultValues_IgnoredWhenVariableDefined()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("DEFINED", "actual_value", VariableSource.Configuration);

        // Act
        var result = expander.Expand("${DEFINED:-default_value}", resolver);

        // Assert
        result.Should().Be("actual_value");
    }

    [Fact]
    public void DefaultValues_CanContainNestedVariables()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("FALLBACK", "fallback_value", VariableSource.Configuration);

        // Act
        var result = expander.Expand("${UNDEFINED:-${FALLBACK}}", resolver);

        // Assert
        result.Should().Be("fallback_value");
    }

    #endregion

    #region Required Variable Tests

    [Fact]
    public void RequiredVariables_ThrowWhenUndefined()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act
        var act = () => expander.Expand("${REQUIRED:?Variable must be set}", resolver);

        // Assert
        act.Should().Throw<VariableException>()
            .WithMessage("*REQUIRED*Variable must be set*");
    }

    [Fact]
    public void RequiredVariables_PassWhenDefined()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("REQUIRED", "is_set", VariableSource.Configuration);

        // Act
        var result = expander.Expand("${REQUIRED:?Variable must be set}", resolver);

        // Assert
        result.Should().Be("is_set");
    }

    #endregion

    #region Escaped Variable Tests

    [Fact]
    public void EscapedVariables_AreNotExpanded()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("VAR", "value", VariableSource.Configuration);

        // Act
        var result = expander.Expand("Normal: ${VAR}, Escaped: \\${VAR}", resolver);

        // Assert
        result.Should().Be("Normal: value, Escaped: ${VAR}");
    }

    [Fact]
    public void EscapedVariables_UsefulForShellScripts()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("BUILD_DIR", "/build", VariableSource.Configuration);

        // Act - Useful for generating shell scripts that need literal ${...}
        var result = expander.Expand("cd ${BUILD_DIR} && echo \\${PATH}", resolver);

        // Assert
        result.Should().Be("cd /build && echo ${PATH}");
    }

    #endregion

    #region Circular Reference Protection Tests

    [Fact]
    public void CircularReferences_AreDetected()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Create circular reference: A -> B -> C -> A
        resolver.SetVariable("A", "${B}", VariableSource.Configuration);
        resolver.SetVariable("B", "${C}", VariableSource.Configuration);
        resolver.SetVariable("C", "${A}", VariableSource.Configuration);

        // Act
        var act = () => expander.Expand("${A}", resolver);

        // Assert
        act.Should().Throw<VariableException>()
            .Which.ErrorCode.Should().Be(PDK.Core.ErrorHandling.ErrorCodes.VariableCircularReference);
    }

    [Fact]
    public void SelfReferences_AreDetected()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("SELF", "${SELF}", VariableSource.Configuration);

        // Act
        var act = () => expander.Expand("${SELF}", resolver);

        // Assert
        act.Should().Throw<VariableException>()
            .Which.ErrorCode.Should().Be(PDK.Core.ErrorHandling.ErrorCodes.VariableCircularReference);
    }

    #endregion

    #region Real-World Scenario Tests

    [Fact]
    public void RealWorldScenario_DockerBuild()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["REGISTRY"] = "ghcr.io",
                ["ORG"] = "myorg",
                ["IMAGE_NAME"] = "myapp",
                ["TAG"] = "latest"
            }
        };

        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.LoadFromConfiguration(config);

        // Override tag from CLI
        resolver.SetVariable("TAG", "v1.2.3", VariableSource.CliArgument);

        // Act
        var imageRef = expander.Expand("${REGISTRY}/${ORG}/${IMAGE_NAME}:${TAG}", resolver);

        // Assert
        imageRef.Should().Be("ghcr.io/myorg/myapp:v1.2.3");
    }

    [Fact]
    public void RealWorldScenario_ConnectionString()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["DB_HOST"] = "localhost",
                ["DB_PORT"] = "5432",
                ["DB_NAME"] = "mydb"
            }
        };

        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.LoadFromConfiguration(config);

        // Simulate environment override for production
        resolver.SetVariable("DB_HOST", "prod-db.example.com", VariableSource.Environment);

        // Act
        var connStr = expander.Expand(
            "postgres://${DB_USER:-admin}:${DB_PASS:-${DB_USER:-admin}}@${DB_HOST}:${DB_PORT}/${DB_NAME}",
            resolver);

        // Assert
        connStr.Should().Be("postgres://admin:admin@prod-db.example.com:5432/mydb");
    }

    [Fact]
    public void RealWorldScenario_PathConstruction()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["PROJECT_ROOT"] = "/app",
                ["SRC_DIR"] = "${PROJECT_ROOT}/src",
                ["TEST_DIR"] = "${PROJECT_ROOT}/tests",
                ["BUILD_DIR"] = "${PROJECT_ROOT}/build"
            }
        };

        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.LoadFromConfiguration(config);

        // Act
        var paths = new Dictionary<string, string>
        {
            ["src"] = expander.Expand("${SRC_DIR}", resolver),
            ["test"] = expander.Expand("${TEST_DIR}", resolver),
            ["build"] = expander.Expand("${BUILD_DIR}", resolver)
        };

        // Assert
        paths["src"].Should().Be("/app/src");
        paths["test"].Should().Be("/app/tests");
        paths["build"].Should().Be("/app/build");
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        for (int i = 0; i < 50; i++)
        {
            resolver.SetVariable($"VAR_{i}", $"value_{i}", VariableSource.Configuration);
        }

        // Act - Concurrent expansion
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            var varIndex = i % 50;
            return expander.Expand($"${{{$"VAR_{varIndex}"}}}", resolver);
        }));

        var results = await Task.WhenAll(tasks);

        // Assert - All expansions should succeed
        results.Should().AllSatisfy(r => r.Should().StartWith("value_"));
    }

    #endregion
}
