var builder = WebApplication.CreateBuilder(args);

// Handle --version flag
if (args.Contains("--version"))
{
    Console.WriteLine("DockerApp version 1.0.0");
    return;
}

var app = builder.Build();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Root endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "DockerApp",
    version = "1.0.0",
    environment = app.Environment.EnvironmentName
}));

// Info endpoint
app.MapGet("/info", () => Results.Ok(new
{
    hostname = Environment.MachineName,
    osVersion = Environment.OSVersion.ToString(),
    processorCount = Environment.ProcessorCount,
    dotnetVersion = Environment.Version.ToString()
}));

Console.WriteLine("DockerApp starting on port 8080...");
app.Run();
