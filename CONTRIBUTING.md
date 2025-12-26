# Contributing to PDK

Thank you for your interest in contributing to PDK (Pipeline Development Kit)! This guide will help you get started.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Making Changes](#making-changes)
- [Testing](#testing)
- [Submitting Changes](#submitting-changes)
- [Code Standards](#code-standards)
- [Getting Help](#getting-help)

## Code of Conduct

PDK follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold this code. Please report unacceptable behavior by opening an issue.

## Getting Started

### Prerequisites

- **.NET 8.0 SDK** or later ([download](https://dotnet.microsoft.com/download))
- **Docker Desktop** (for integration tests and Docker runner)
- **Git** for version control
- A code editor (Visual Studio, VS Code, or JetBrains Rider)

### Development Setup

1. **Fork and clone the repository:**

```bash
git clone https://github.com/AdamWyatt34/pdk.git
cd pdk
```

2. **Restore dependencies:**

```bash
dotnet restore
```

3. **Build the project:**

```bash
dotnet build
```

4. **Run tests:**

```bash
dotnet test
```

5. **Run PDK locally:**

```bash
dotnet run --project src/PDK.CLI/PDK.CLI.csproj -- run --file path/to/pipeline.yml
```

For detailed setup instructions, see [Development Setup](docs/developers/setup.md).

## Making Changes

### Finding Issues to Work On

- **Good First Issues:** Look for issues labeled [`good first issue`](https://github.com/AdamWyatt34/pdk/labels/good%20first%20issue)
- **Help Wanted:** Check issues labeled [`help wanted`](https://github.com/AdamWyatt34/pdk/labels/help%20wanted)
- **Your Ideas:** Feel free to propose new features via [GitHub Discussions](https://github.com/AdamWyatt34/pdk/discussions)

### Branching Strategy

1. Create a branch from `main`:

```bash
git checkout -b feature/my-feature
# or
git checkout -b fix/issue-123
```

2. Branch naming conventions:
   - Features: `feature/description`
   - Bug fixes: `fix/description` or `fix/issue-123`
   - Documentation: `docs/description`
   - Refactoring: `refactor/description`

### Making Commits

Write clear, concise commit messages following this format:

```
<type>: <subject>

<body>

<footer>
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `test`: Adding or updating tests
- `refactor`: Code refactoring
- `perf`: Performance improvements
- `chore`: Build process or tooling changes

**Example:**

```
feat: add support for GitLab CI pipelines

Implement GitLabCIParser to parse .gitlab-ci.yml files.
Maps GitLab CI jobs and stages to common pipeline model.

Closes #123
```

## Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run unit tests only
dotnet test --filter Category=Unit

# Run integration tests only
dotnet test --filter Category=Integration

# Run with coverage
dotnet test /p:CollectCoverage=true
```

### Writing Tests

All new code should include tests:

- **Unit tests:** Required for all new logic
- **Integration tests:** Required for end-to-end scenarios
- **Coverage target:** 80% minimum

Example unit test:

```csharp
public class GitHubActionsParserTests
{
    [Fact]
    public async Task ParseAsync_ValidWorkflow_ReturnsPipeline()
    {
        // Arrange
        var parser = new GitHubActionsParser();
        var filePath = "test-workflow.yml";

        // Act
        var pipeline = await parser.ParseFile(filePath);

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Jobs.Should().HaveCount(1);
    }
}
```

See [Testing Guide](docs/developers/testing.md) for more details.

## Submitting Changes

### Before Submitting

Ensure your code:
- Builds without errors or warnings
- Passes all existing tests
- Includes tests for new functionality
- Follows code standards (see below)
- Includes XML documentation for public APIs
- Updates relevant documentation

### Creating a Pull Request

1. **Push your branch:**

```bash
git push origin feature/my-feature
```

2. **Create PR on GitHub:**
   - Provide clear title and description
   - Reference related issues
   - Describe what changed and why
   - Include testing instructions
   - Add screenshots for UI changes

3. **PR will be created using our template** which includes:
   - Description section
   - Related issues
   - Testing checklist
   - Review checklist

### Code Review Process

1. **Automated checks:** CI/CD will run tests and checks
2. **Reviewer assigned:** A maintainer will review your code
3. **Address feedback:** Make requested changes
4. **Approval:** Once approved, your PR will be merged

**What reviewers look for:**
- Correctness of implementation
- Test coverage
- Code quality and clarity
- Performance implications
- Security considerations
- Documentation completeness

## Code Standards

### C# Style Guide

We follow standard .NET conventions:

**Naming:**
- Classes: `PascalCase`
- Methods: `PascalCase`
- Parameters: `camelCase`
- Private fields: `_camelCase`
- Constants: `PascalCase`
- Interfaces: `IPascalCase`

**Formatting:**
- File-scoped namespaces
- 4-space indentation
- Allman brace style
- One class per file

**Modern C# Features:**
- Use primary constructors where appropriate
- Use record types for immutable data
- Use pattern matching
- Use null-coalescing operators

**Example:**

```csharp
namespace PDK.Core.Models;

public record Pipeline
{
    public required string Name { get; init; }
    public required IList<Job> Jobs { get; init; }

    public Job? FindJob(string jobId)
    {
        return Jobs.FirstOrDefault(j => j.Id == jobId);
    }
}
```

### Documentation Standards

All public APIs require XML documentation:

```csharp
/// <summary>
/// Parses a GitHub Actions workflow file.
/// </summary>
/// <param name="filePath">Path to the workflow YAML file.</param>
/// <returns>The parsed pipeline.</returns>
/// <exception cref="ParserException">Thrown when parsing fails.</exception>
public async Task<Pipeline> ParseAsync(string filePath)
{
    // Implementation
}
```

### Testing Standards

- Unit tests use xUnit
- Use FluentAssertions for assertions
- Use Moq for mocking
- Tests should be independent and repeatable
- Test method names: `MethodName_Scenario_ExpectedResult`

For complete standards, see [Code Standards](docs/developers/code-standards.md).

## Types of Contributions

### Bug Fixes

1. Check if bug is already reported
2. Create issue if needed (include reproduction steps)
3. Fix the bug
4. Add regression test
5. Submit PR referencing the issue

### New Features

1. Start discussion in [GitHub Discussions](https://github.com/AdamWyatt34/pdk/discussions)
2. Get feedback from maintainers
3. Create feature branch
4. Implement feature with tests and docs
5. Submit PR

**Large features** should be broken into smaller PRs when possible.

### Documentation

Documentation improvements are always welcome:
- Fix typos or errors
- Add examples
- Clarify confusing sections
- Add missing documentation

### Performance Optimizations

1. Identify bottleneck with profiling
2. Propose optimization (discuss first if significant)
3. Include benchmarks showing improvement
4. Ensure no regression in functionality

## Getting Help

### Communication Channels

- **GitHub Issues:** Bug reports and feature requests
- **GitHub Discussions:** Questions and general discussion
- **Pull Requests:** Code review and collaboration

### Asking Questions

When asking for help:
- Search existing issues/discussions first
- Provide context and details
- Include code samples and error messages
- Be patient and respectful

### Reporting Bugs

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md):
- PDK version
- .NET version
- Operating system
- Steps to reproduce
- Expected vs actual behavior
- Logs or error messages

### Suggesting Features

Use the [feature request template](.github/ISSUE_TEMPLATE/feature_request.md):
- Clear description of the feature
- Use case / motivation
- Proposed implementation (optional)
- Alternatives considered

## Recognition

Contributors are recognized in:
- Release notes
- GitHub contributor graph

Thank you for contributing to PDK!

## Additional Resources

- [Architecture Documentation](docs/developers/architecture/)
- [Development Setup Guide](docs/developers/setup.md)
- [Testing Guide](docs/developers/testing.md)
- [Extension Guide](docs/developers/extending/)
