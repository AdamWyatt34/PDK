using FluentAssertions;
using PDK.Core.Configuration;
using PDK.Core.ErrorHandling;

namespace PDK.Tests.Unit.Configuration;

public class ConfigurationExceptionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        // Arrange
        var errorCode = "TEST-001";
        var message = "Test message";
        var configFilePath = "/path/to/config.json";
        var validationErrors = new List<ValidationError>
        {
            new() { Path = "variables.TEST", Message = "Invalid value" }
        };

        // Act
        var exception = new ConfigurationException(
            errorCode,
            message,
            configFilePath,
            validationErrors: validationErrors);

        // Assert
        exception.ErrorCode.Should().Be(errorCode);
        exception.Message.Should().Be(message);
        exception.ConfigFilePath.Should().Be(configFilePath);
        exception.ValidationErrors.Should().HaveCount(1);
    }

    [Fact]
    public void Constructor_WithNullValidationErrors_ReturnsEmptyList()
    {
        // Act
        var exception = new ConfigurationException(
            "TEST-001",
            "Test message");

        // Assert
        exception.ValidationErrors.Should().NotBeNull();
        exception.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithInnerException_PreservesInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ConfigurationException(
            "TEST-001",
            "Test message",
            innerException: innerException);

        // Assert
        exception.InnerException.Should().Be(innerException);
    }

    #endregion

    #region FileNotFound Tests

    [Fact]
    public void FileNotFound_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/missing.json";

        // Act
        var exception = ConfigurationException.FileNotFound(path);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigFileNotFound);
    }

    [Fact]
    public void FileNotFound_ReturnsExceptionWithPath()
    {
        // Arrange
        var path = "/path/to/missing.json";

        // Act
        var exception = ConfigurationException.FileNotFound(path);

        // Assert
        exception.ConfigFilePath.Should().Be(path);
        exception.Message.Should().Contain(path);
    }

    [Fact]
    public void FileNotFound_IncludesHelpfulSuggestions()
    {
        // Arrange
        var path = "/path/to/missing.json";

        // Act
        var exception = ConfigurationException.FileNotFound(path);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("Verify the file path"));
        exception.Suggestions.Should().Contain(s => s.Contains("pdk init"));
    }

    #endregion

    #region InvalidJson Tests

    [Fact]
    public void InvalidJson_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/invalid.json";
        var innerException = new Exception("Unexpected token");

        // Act
        var exception = ConfigurationException.InvalidJson(path, innerException);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigInvalidJson);
    }

    [Fact]
    public void InvalidJson_IncludesInnerExceptionMessage()
    {
        // Arrange
        var path = "/path/to/invalid.json";
        var innerException = new Exception("Unexpected token at position 42");

        // Act
        var exception = ConfigurationException.InvalidJson(path, innerException);

        // Assert
        exception.Message.Should().Contain("Unexpected token at position 42");
        exception.InnerException.Should().Be(innerException);
    }

    [Fact]
    public void InvalidJson_IncludesPath()
    {
        // Arrange
        var path = "/path/to/invalid.json";
        var innerException = new Exception("Parse error");

        // Act
        var exception = ConfigurationException.InvalidJson(path, innerException);

        // Assert
        exception.ConfigFilePath.Should().Be(path);
        exception.Message.Should().Contain(path);
    }

    [Fact]
    public void InvalidJson_IncludesHelpfulSuggestions()
    {
        // Arrange
        var path = "/path/to/invalid.json";
        var innerException = new Exception("Error");

        // Act
        var exception = ConfigurationException.InvalidJson(path, innerException);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("JSON validator"));
        exception.Suggestions.Should().Contain(s => s.Contains("trailing commas"));
    }

    #endregion

    #region ValidationFailed Tests

    [Fact]
    public void ValidationFailed_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/config.json";
        var errors = new List<ValidationError>
        {
            new() { Path = "variables.TEST", Message = "Invalid value" }
        };

        // Act
        var exception = ConfigurationException.ValidationFailed(path, errors);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigValidationFailed);
    }

    [Fact]
    public void ValidationFailed_IncludesAllErrorsInMessage()
    {
        // Arrange
        var path = "/path/to/config.json";
        var errors = new List<ValidationError>
        {
            new() { Path = "variables.VAR1", Message = "First error" },
            new() { Path = "logging.level", Message = "Second error" }
        };

        // Act
        var exception = ConfigurationException.ValidationFailed(path, errors);

        // Assert
        exception.Message.Should().Contain("variables.VAR1");
        exception.Message.Should().Contain("First error");
        exception.Message.Should().Contain("logging.level");
        exception.Message.Should().Contain("Second error");
    }

    [Fact]
    public void ValidationFailed_StoresValidationErrors()
    {
        // Arrange
        var path = "/path/to/config.json";
        var errors = new List<ValidationError>
        {
            new() { Path = "variables.VAR1", Message = "First error" },
            new() { Path = "logging.level", Message = "Second error" }
        };

        // Act
        var exception = ConfigurationException.ValidationFailed(path, errors);

        // Assert
        exception.ValidationErrors.Should().HaveCount(2);
        exception.ValidationErrors[0].Path.Should().Be("variables.VAR1");
        exception.ValidationErrors[1].Path.Should().Be("logging.level");
    }

    [Fact]
    public void ValidationFailed_WithEmptyErrors_ReturnsValidException()
    {
        // Arrange
        var path = "/path/to/config.json";
        var errors = new List<ValidationError>();

        // Act
        var exception = ConfigurationException.ValidationFailed(path, errors);

        // Assert
        exception.Should().NotBeNull();
        exception.ValidationErrors.Should().BeEmpty();
    }

    #endregion

    #region InvalidVersion Tests

    [Fact]
    public void InvalidVersion_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/config.json";
        var version = "2.0";

        // Act
        var exception = ConfigurationException.InvalidVersion(path, version);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigInvalidVersion);
    }

    [Fact]
    public void InvalidVersion_IncludesVersionInMessage()
    {
        // Arrange
        var path = "/path/to/config.json";
        var version = "2.0";

        // Act
        var exception = ConfigurationException.InvalidVersion(path, version);

        // Assert
        exception.Message.Should().Contain("2.0");
        exception.Message.Should().Contain("1.0"); // Expected version
    }

    [Fact]
    public void InvalidVersion_WithNullVersion_ShowsMissingMessage()
    {
        // Arrange
        var path = "/path/to/config.json";

        // Act
        var exception = ConfigurationException.InvalidVersion(path, null);

        // Assert
        exception.Message.Should().Contain("(missing)");
    }

    [Fact]
    public void InvalidVersion_IncludesHelpfulSuggestions()
    {
        // Arrange
        var path = "/path/to/config.json";
        var version = "2.0";

        // Act
        var exception = ConfigurationException.InvalidVersion(path, version);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("1.0"));
    }

    #endregion

    #region InvalidVariableName Tests

    [Fact]
    public void InvalidVariableName_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/config.json";
        var variableName = "invalid-name";

        // Act
        var exception = ConfigurationException.InvalidVariableName(path, variableName);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigInvalidVariableName);
    }

    [Fact]
    public void InvalidVariableName_IncludesVariableNameInMessage()
    {
        // Arrange
        var path = "/path/to/config.json";
        var variableName = "invalid-name";

        // Act
        var exception = ConfigurationException.InvalidVariableName(path, variableName);

        // Assert
        exception.Message.Should().Contain("invalid-name");
        exception.ConfigFilePath.Should().Be(path);
    }

    [Fact]
    public void InvalidVariableName_IncludesPatternSuggestion()
    {
        // Arrange
        var path = "/path/to/config.json";
        var variableName = "invalid-name";

        // Act
        var exception = ConfigurationException.InvalidVariableName(path, variableName);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("uppercase"));
    }

    #endregion

    #region InvalidMemoryLimit Tests

    [Fact]
    public void InvalidMemoryLimit_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/config.json";
        var memoryLimit = "invalid";

        // Act
        var exception = ConfigurationException.InvalidMemoryLimit(path, memoryLimit);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigInvalidMemoryLimit);
    }

    [Fact]
    public void InvalidMemoryLimit_IncludesValueInMessage()
    {
        // Arrange
        var path = "/path/to/config.json";
        var memoryLimit = "invalid";

        // Act
        var exception = ConfigurationException.InvalidMemoryLimit(path, memoryLimit);

        // Assert
        exception.Message.Should().Contain("invalid");
        exception.ConfigFilePath.Should().Be(path);
    }

    [Fact]
    public void InvalidMemoryLimit_IncludesExampleSuggestions()
    {
        // Arrange
        var path = "/path/to/config.json";
        var memoryLimit = "invalid";

        // Act
        var exception = ConfigurationException.InvalidMemoryLimit(path, memoryLimit);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("512m") || s.Contains("2g"));
    }

    #endregion

    #region InvalidCpuLimit Tests

    [Fact]
    public void InvalidCpuLimit_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/config.json";
        var cpuLimit = 0.01;

        // Act
        var exception = ConfigurationException.InvalidCpuLimit(path, cpuLimit);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigInvalidCpuLimit);
    }

    [Fact]
    public void InvalidCpuLimit_IncludesValueInMessage()
    {
        // Arrange
        var path = "/path/to/config.json";
        var cpuLimit = 0.05;

        // Act
        var exception = ConfigurationException.InvalidCpuLimit(path, cpuLimit);

        // Assert
        exception.Message.Should().Contain("0.05");
        exception.Message.Should().Contain("0.1"); // Minimum value
        exception.ConfigFilePath.Should().Be(path);
    }

    [Fact]
    public void InvalidCpuLimit_IncludesExampleSuggestions()
    {
        // Arrange
        var path = "/path/to/config.json";
        var cpuLimit = 0.01;

        // Act
        var exception = ConfigurationException.InvalidCpuLimit(path, cpuLimit);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("0.5") || s.Contains("2.0"));
    }

    #endregion

    #region InvalidLogLevel Tests

    [Fact]
    public void InvalidLogLevel_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/config.json";
        var logLevel = "InvalidLevel";

        // Act
        var exception = ConfigurationException.InvalidLogLevel(path, logLevel);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigInvalidLogLevel);
    }

    [Fact]
    public void InvalidLogLevel_IncludesValueInMessage()
    {
        // Arrange
        var path = "/path/to/config.json";
        var logLevel = "Trace";

        // Act
        var exception = ConfigurationException.InvalidLogLevel(path, logLevel);

        // Assert
        exception.Message.Should().Contain("Trace");
        exception.ConfigFilePath.Should().Be(path);
    }

    [Fact]
    public void InvalidLogLevel_IncludesValidLevelsSuggestion()
    {
        // Arrange
        var path = "/path/to/config.json";
        var logLevel = "InvalidLevel";

        // Act
        var exception = ConfigurationException.InvalidLogLevel(path, logLevel);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s =>
            s.Contains("Info") || s.Contains("Debug") || s.Contains("Warning") || s.Contains("Error"));
    }

    #endregion

    #region InvalidRetentionDays Tests

    [Fact]
    public void InvalidRetentionDays_ReturnsExceptionWithCorrectErrorCode()
    {
        // Arrange
        var path = "/path/to/config.json";
        var retentionDays = -1;

        // Act
        var exception = ConfigurationException.InvalidRetentionDays(path, retentionDays);

        // Assert
        exception.ErrorCode.Should().Be(ErrorCodes.ConfigInvalidRetentionDays);
    }

    [Fact]
    public void InvalidRetentionDays_IncludesValueInMessage()
    {
        // Arrange
        var path = "/path/to/config.json";
        var retentionDays = -5;

        // Act
        var exception = ConfigurationException.InvalidRetentionDays(path, retentionDays);

        // Assert
        exception.Message.Should().Contain("-5");
        exception.ConfigFilePath.Should().Be(path);
    }

    [Fact]
    public void InvalidRetentionDays_IncludesHelpfulSuggestions()
    {
        // Arrange
        var path = "/path/to/config.json";
        var retentionDays = -1;

        // Act
        var exception = ConfigurationException.InvalidRetentionDays(path, retentionDays);

        // Assert
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("0") || s.Contains("positive"));
    }

    #endregion
}
