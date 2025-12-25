namespace PDK.Tests.Performance.Infrastructure;

/// <summary>
/// Manages temporary workspace creation and cleanup for benchmarks.
/// </summary>
public static class BenchmarkWorkspaceSetup
{
    private static readonly string BenchmarkTempRoot = Path.Combine(
        Path.GetTempPath(),
        "pdk-benchmarks");

    /// <summary>
    /// Creates a temporary workspace directory for benchmark execution.
    /// </summary>
    /// <returns>The path to the created workspace.</returns>
    public static string CreateTempWorkspace()
    {
        var workspacePath = Path.Combine(
            BenchmarkTempRoot,
            $"workspace-{Guid.NewGuid():N}");

        Directory.CreateDirectory(workspacePath);

        // Create minimal project structure for realistic benchmarks
        CreateMinimalProjectStructure(workspacePath);

        return workspacePath;
    }

    /// <summary>
    /// Creates a minimal project structure for testing.
    /// </summary>
    private static void CreateMinimalProjectStructure(string workspacePath)
    {
        // Create a simple .NET project structure
        var srcPath = Path.Combine(workspacePath, "src");
        Directory.CreateDirectory(srcPath);

        File.WriteAllText(
            Path.Combine(srcPath, "Program.cs"),
            """
            Console.WriteLine("Hello, World!");
            """);

        File.WriteAllText(
            Path.Combine(workspacePath, "sample.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        // Create package.json for npm benchmarks
        File.WriteAllText(
            Path.Combine(workspacePath, "package.json"),
            """
            {
              "name": "benchmark-sample",
              "version": "1.0.0",
              "scripts": {
                "build": "echo Building...",
                "test": "echo Testing...",
                "lint": "echo Linting..."
              }
            }
            """);

        // Create a simple README
        File.WriteAllText(
            Path.Combine(workspacePath, "README.md"),
            "# Benchmark Sample Project\n\nThis is a sample project for PDK benchmarks.");
    }

    /// <summary>
    /// Cleans up a temporary workspace directory.
    /// </summary>
    /// <param name="workspacePath">The path to clean up.</param>
    public static void CleanupWorkspace(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath))
            return;

        try
        {
            Directory.Delete(workspacePath, recursive: true);
        }
        catch (Exception)
        {
            // Ignore cleanup errors in benchmarks
        }
    }

    /// <summary>
    /// Cleans up all benchmark workspaces.
    /// </summary>
    public static void CleanupAllWorkspaces()
    {
        if (Directory.Exists(BenchmarkTempRoot))
        {
            try
            {
                Directory.Delete(BenchmarkTempRoot, recursive: true);
            }
            catch (Exception)
            {
                // Ignore cleanup errors
            }
        }
    }
}
