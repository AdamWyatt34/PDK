namespace PDK.Tests.Integration;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PDK.Core.Configuration;
using PDK.Core.Logging;
using PDK.Core.Secrets;
using PDK.Core.Variables;

/// <summary>
/// End-to-end integration tests for Sprint 7 Configuration, Variables, and Secrets.
/// </summary>
public class Sprint7EndToEndTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogger<ConfigurationLoader> _mockLogger;

    public Sprint7EndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdk-sprint7-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _mockLogger = loggerFactory.CreateLogger<ConfigurationLoader>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    #region Configuration Loading in Pipeline Execution

    [Fact]
    public async Task Configuration_LoadedDuringPipelineExecution()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "pdk.config.json");
        File.WriteAllText(configPath, """
            {
                "version": "1.0",
                "variables": {
                    "BUILD_CONFIG": "Release",
                    "VERSION": "2.0.0"
                }
            }
            """);

        var loader = new ConfigurationLoader(_mockLogger);
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act
        var config = await loader.LoadAsync(configPath);
        resolver.LoadFromConfiguration(config!);

        // Assert
        var result = expander.Expand("Building ${BUILD_CONFIG} v${VERSION}", resolver);
        result.Should().Be("Building Release v2.0.0");
    }

    #endregion

    #region CLI Variables Override Configuration

    [Fact]
    public void CliVariables_OverrideConfiguration()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["BUILD_CONFIG"] = "Debug",
                ["VERSION"] = "1.0.0"
            }
        };

        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        resolver.LoadFromConfiguration(config);

        // CLI override
        resolver.SetVariable("BUILD_CONFIG", "Release", VariableSource.CliArgument);

        // Act
        var configValue = expander.Expand("${BUILD_CONFIG}", resolver);
        var unchangedValue = expander.Expand("${VERSION}", resolver);

        // Assert
        configValue.Should().Be("Release", "CLI should override config");
        unchangedValue.Should().Be("1.0.0", "Non-overridden should use config");
    }

    #endregion

    #region Secrets Masked in Output

    [Fact]
    public void Secrets_MaskedInOutput()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("super-secret-token-12345");
        masker.RegisterSecret("another-secret");

        var output = "Deploying with token: super-secret-token-12345 to server";

        // Act
        var masked = masker.MaskSecrets(output);

        // Assert
        masked.Should().Be("Deploying with token: *** to server");
        masked.Should().NotContain("super-secret-token-12345");
    }

    [Fact]
    public void Secrets_MaskedCaseInsensitive()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("MySecretValue");

        var output = "Value is MYSECRETVALUE and mysecretvalue";

        // Act
        var masked = masker.MaskSecrets(output);

        // Assert
        masked.Should().Be("Value is *** and ***");
    }

    #endregion

    #region Environment Variable Patterns

    [Fact]
    public void PdkVar_PrefixStripped()
    {
        // Arrange
        var testVarName = $"PDK_VAR_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(testVarName, "test_value");

        try
        {
            var resolver = new VariableResolver();
            var expander = new VariableExpander();
            resolver.LoadFromEnvironment();

            // Extract just the variable name without PDK_VAR_ prefix
            var varName = testVarName.Replace("PDK_VAR_", "");

            // Act
            var result = expander.Expand($"${{{varName}}}", resolver);

            // Assert
            result.Should().Be("test_value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(testVarName, null);
        }
    }

    [Fact]
    public void PdkSecret_PrefixStripped_AndTreatedAsSecret()
    {
        // Arrange
        var testVarName = $"PDK_SECRET_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(testVarName, "secret_value");

        try
        {
            var resolver = new VariableResolver();
            resolver.LoadFromEnvironment();

            // Extract just the variable name without PDK_SECRET_ prefix
            var varName = testVarName.Replace("PDK_SECRET_", "");

            // Act
            var value = resolver.Resolve(varName);
            var source = resolver.GetSource(varName);

            // Assert
            value.Should().Be("secret_value");
            source.Should().Be(VariableSource.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(testVarName, null);
        }
    }

    #endregion

    #region Nested Variable Expansion

    [Fact]
    public void NestedVariables_ExpandCorrectly()
    {
        // Arrange
        var config = new PdkConfig
        {
            Variables = new Dictionary<string, string>
            {
                ["REGISTRY"] = "ghcr.io",
                ["ORG"] = "myorg",
                ["IMAGE"] = "${REGISTRY}/${ORG}/myapp",
                ["FULL_REF"] = "${IMAGE}:${TAG:-latest}"
            }
        };

        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.LoadFromConfiguration(config);

        // Act
        var result = expander.Expand("docker push ${FULL_REF}", resolver);

        // Assert
        result.Should().Be("docker push ghcr.io/myorg/myapp:latest");
    }

    #endregion

    #region Required Variable Errors

    [Fact]
    public void RequiredVariable_ThrowsWhenMissing()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act
        Action act = () => expander.Expand("${MISSING_VAR:?MISSING_VAR must be set}", resolver);

        // Assert
        act.Should().Throw<VariableException>()
            .WithMessage("*MISSING_VAR*must be set*");
    }

    [Fact]
    public void RequiredVariable_PassesWhenDefined()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("DEFINED_VAR", "some_value", VariableSource.Configuration);

        // Act
        var result = expander.Expand("${DEFINED_VAR:?Must be set}", resolver);

        // Assert
        result.Should().Be("some_value");
    }

    #endregion

    #region Full Integration Workflow

    [Fact]
    public async Task FullWorkflow_ConfigToExpansion()
    {
        // Arrange - Create config file
        var configPath = Path.Combine(_tempDir, ".pdkrc");
        File.WriteAllText(configPath, """
            {
                "version": "1.0",
                "variables": {
                    "BASE_URL": "https://api.example.com",
                    "ENV": "staging"
                }
            }
            """);

        // Act - Full workflow
        var loader = new ConfigurationLoader(_mockLogger);
        var merger = new ConfigurationMerger();
        var masker = new SecretMasker();
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // 1. Load config
        var config = await loader.LoadAsync(configPath);
        var defaults = DefaultConfiguration.Create();
        var merged = merger.Merge(defaults, config!);

        // 2. Load variables from config
        resolver.LoadFromConfiguration(merged);

        // 3. Apply CLI override
        resolver.SetVariable("ENV", "production", VariableSource.CliArgument);

        // 4. Simulate secret
        resolver.SetVariable("API_TOKEN", "secret-token-xyz", VariableSource.Secret);
        masker.RegisterSecret("secret-token-xyz");

        // 5. Expand a command
        var command = expander.Expand(
            "curl -H 'Authorization: ${API_TOKEN}' ${BASE_URL}/${ENV}/deploy",
            resolver);

        // 6. Mask secrets
        var maskedCommand = masker.MaskSecrets(command);

        // Assert
        command.Should().Be("curl -H 'Authorization: secret-token-xyz' https://api.example.com/production/deploy");
        maskedCommand.Should().Be("curl -H 'Authorization: ***' https://api.example.com/production/deploy");
    }

    #endregion

    #region Secret Manager Integration

    [Fact]
    public async Task SecretManager_StoresAndRetrievesSecrets()
    {
        // Arrange
        var storagePath = Path.Combine(_tempDir, "secrets.json");
        var storage = new SecretStorage(storagePath);
        var encryption = new SecretEncryption();
        var masker = new SecretMasker();
        var manager = new SecretManager(encryption, storage, masker);

        // Act
        await manager.SetSecretAsync("TEST_SECRET", "my-secret-value");
        var retrieved = await manager.GetSecretAsync("TEST_SECRET");
        var exists = await manager.SecretExistsAsync("TEST_SECRET");
        var names = await manager.ListSecretNamesAsync();

        // Assert
        retrieved.Should().Be("my-secret-value");
        exists.Should().BeTrue();
        names.Should().Contain("TEST_SECRET");

        // Verify masker was updated
        var masked = masker.MaskSecrets("Value is my-secret-value here");
        masked.Should().Be("Value is *** here");
    }

    #endregion

    #region Variable Context Updates

    [Fact]
    public void VariableContext_UpdatesBuiltInVariables()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act - Update context with job/step
        resolver.UpdateContext(new VariableContext
        {
            Workspace = "/my/workspace",
            Runner = "ubuntu-latest",
            JobName = "build",
            StepName = "compile"
        });

        var result = expander.Expand(
            "Job: ${PDK_JOB}, Step: ${PDK_STEP}, Runner: ${PDK_RUNNER}",
            resolver);

        // Assert
        result.Should().Be("Job: build, Step: compile, Runner: ubuntu-latest");
    }

    #endregion

    #region Variable Precedence

    [Fact]
    public void VariablePrecedence_CliOverridesAll()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Set same variable from multiple sources
        resolver.SetVariable("VAR", "from-config", VariableSource.Configuration);
        resolver.SetVariable("VAR", "from-env", VariableSource.Environment);
        resolver.SetVariable("VAR", "from-secret", VariableSource.Secret);
        resolver.SetVariable("VAR", "from-cli", VariableSource.CliArgument);

        // Act
        var result = expander.Expand("${VAR}", resolver);
        var source = resolver.GetSource("VAR");

        // Assert
        result.Should().Be("from-cli");
        source.Should().Be(VariableSource.CliArgument);
    }

    [Fact]
    public void VariablePrecedence_SecretOverridesEnvironment()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Set same variable from multiple sources (no CLI)
        resolver.SetVariable("VAR", "from-config", VariableSource.Configuration);
        resolver.SetVariable("VAR", "from-env", VariableSource.Environment);
        resolver.SetVariable("VAR", "from-secret", VariableSource.Secret);

        // Act
        var result = expander.Expand("${VAR}", resolver);
        var source = resolver.GetSource("VAR");

        // Assert
        result.Should().Be("from-secret");
        source.Should().Be(VariableSource.Secret);
    }

    #endregion

    #region Default Values

    [Fact]
    public void DefaultValue_UsedWhenVariableNotSet()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();

        // Act
        var result = expander.Expand("${UNDEFINED_VAR:-default_value}", resolver);

        // Assert
        result.Should().Be("default_value");
    }

    [Fact]
    public void DefaultValue_NotUsedWhenVariableIsSet()
    {
        // Arrange
        var resolver = new VariableResolver();
        var expander = new VariableExpander();
        resolver.SetVariable("DEFINED_VAR", "actual_value", VariableSource.Configuration);

        // Act
        var result = expander.Expand("${DEFINED_VAR:-default_value}", resolver);

        // Assert
        result.Should().Be("actual_value");
    }

    #endregion
}
