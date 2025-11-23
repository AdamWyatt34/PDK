using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class ContainerNameGeneratorTests
{
    #region Basic Generation

    [Fact]
    public void GenerateName_SimpleJobName_ReturnsValidName()
    {
        // Arrange
        var jobName = "build";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("build");
        result.Should().StartWith("pdk-");
    }

    [Fact]
    public void GenerateName_EmptyJobName_UsesDefaultJob()
    {
        // Arrange
        var jobName = "";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("job");
        result.Should().StartWith("pdk-job-");
    }

    [Fact]
    public void GenerateName_NullJobName_UsesDefaultJob()
    {
        // Arrange
        string? jobName = null;

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName!);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("job");
        result.Should().StartWith("pdk-job-");
    }

    [Fact]
    public void GenerateName_WhitespaceJobName_UsesDefaultJob()
    {
        // Arrange
        var jobName = "   ";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("job");
        result.Should().StartWith("pdk-job-");
    }

    [Fact]
    public void GenerateName_MultipleCallsSameJob_GeneratesDifferentNames()
    {
        // Arrange
        var jobName = "test";

        // Act
        var result1 = ContainerNameGenerator.GenerateName(jobName);
        Thread.Sleep(1000); // Wait to ensure different timestamp
        var result2 = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result1.Should().NotBe(result2);
    }

    #endregion

    #region Sanitization

    [Fact]
    public void GenerateName_JobNameWithSpaces_RemovesSpaces()
    {
        // Arrange
        var jobName = "Build Job";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().NotContain(" ");
        result.Should().Contain("buildjob");
    }

    [Fact]
    public void GenerateName_JobNameWithUnderscores_RemovesUnderscores()
    {
        // Arrange
        var jobName = "build_job";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().NotContain("_");
        result.Should().Contain("buildjob");
    }

    [Fact]
    public void GenerateName_JobNameWithSpecialChars_RemovesSpecialChars()
    {
        // Arrange
        var jobName = "build@job#test!";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().NotContain("@");
        result.Should().NotContain("#");
        result.Should().NotContain("!");
        result.Should().Contain("buildjobtest");
    }

    [Fact]
    public void GenerateName_UppercaseJobName_ConvertsToLowercase()
    {
        // Arrange
        var jobName = "BUILD";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().Contain("build");
        result.Should().NotContain("BUILD");
    }

    [Fact]
    public void GenerateName_LeadingHyphens_TrimsHyphens()
    {
        // Arrange
        var jobName = "---build";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().Contain("build");
        result.Should().NotStartWith("pdk---");
    }

    [Fact]
    public void GenerateName_TrailingHyphens_TrimsHyphens()
    {
        // Arrange
        var jobName = "build---";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().Contain("build");
        // Should not end with multiple hyphens before timestamp
        result.Should().MatchRegex(@"pdk-build-\d{8}-\d{6}-[a-f0-9]{6}");
    }

    #endregion

    #region Length Constraints

    [Fact]
    public void GenerateName_VeryLongJobName_TruncatesTo63Chars()
    {
        // Arrange
        var longJobName = new string('a', 100);

        // Act
        var result = ContainerNameGenerator.GenerateName(longJobName);

        // Assert
        result.Length.Should().BeLessOrEqualTo(63);
    }

    [Fact]
    public void GenerateName_Output_NeverExceeds63Chars()
    {
        // Arrange
        var testNames = new[]
        {
            "short",
            "medium-length-job",
            new string('x', 50),
            new string('y', 100),
            "job-with-many-hyphens-and-words-in-it"
        };

        // Act & Assert
        foreach (var jobName in testNames)
        {
            var result = ContainerNameGenerator.GenerateName(jobName);
            result.Length.Should().BeLessOrEqualTo(63, $"job name '{jobName}' generated '{result}' which exceeds 63 characters");
        }
    }

    [Fact]
    public void GenerateName_Output_MeetsDockerLengthRequirement()
    {
        // Arrange
        var jobName = "test";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Length.Should().BeInRange(1, 63);
    }

    #endregion

    #region Format Validation

    [Fact]
    public void GenerateName_Output_StartsWithPdk()
    {
        // Arrange
        var jobName = "myJob";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().StartWith("pdk-");
    }

    [Fact]
    public void GenerateName_Output_IsLowercase()
    {
        // Arrange
        var jobName = "MyJob";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().Be(result.ToLowerInvariant());
    }

    [Fact]
    public void GenerateName_Output_IncludesJobName()
    {
        // Arrange
        var jobName = "integration";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        result.Should().Contain("integration");
    }

    [Fact]
    public void GenerateName_Output_IncludesTimestamp()
    {
        // Arrange
        var jobName = "test";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        // Should match pattern: pdk-{name}-{timestamp}-{random}
        // Timestamp format: yyyyMMdd-HHmmss (15 chars)
        result.Should().MatchRegex(@"pdk-\w+-\d{8}-\d{6}-[a-f0-9]{6}");
    }

    [Fact]
    public void GenerateName_Output_IncludesRandomId()
    {
        // Arrange
        var jobName = "test";

        // Act
        var result = ContainerNameGenerator.GenerateName(jobName);

        // Assert
        // Should end with 6-character hex ID
        result.Should().MatchRegex(@"[a-f0-9]{6}$");
    }

    #endregion

    #region IsValidContainerName Tests

    [Fact]
    public void IsValidContainerName_ValidName_ReturnsTrue()
    {
        // Arrange
        var validName = "pdk-test-20241123-143022-a3f5c8";

        // Act
        var result = ContainerNameGenerator.IsValidContainerName(validName);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidContainerName_TooLong_ReturnsFalse()
    {
        // Arrange
        var longName = new string('a', 64);

        // Act
        var result = ContainerNameGenerator.IsValidContainerName(longName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidContainerName_StartsWithHyphen_ReturnsFalse()
    {
        // Arrange
        var invalidName = "-pdk-test-123";

        // Act
        var result = ContainerNameGenerator.IsValidContainerName(invalidName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidContainerName_EndsWithHyphen_ReturnsFalse()
    {
        // Arrange
        var invalidName = "pdk-test-123-";

        // Act
        var result = ContainerNameGenerator.IsValidContainerName(invalidName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidContainerName_UppercaseLetters_ReturnsFalse()
    {
        // Arrange
        var invalidName = "pdk-Test-123";

        // Act
        var result = ContainerNameGenerator.IsValidContainerName(invalidName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidContainerName_EmptyString_ReturnsFalse()
    {
        // Arrange
        var emptyName = "";

        // Act
        var result = ContainerNameGenerator.IsValidContainerName(emptyName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidContainerName_SpecialCharacters_ReturnsFalse()
    {
        // Arrange
        var invalidName = "pdk-test@123";

        // Act
        var result = ContainerNameGenerator.IsValidContainerName(invalidName);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
