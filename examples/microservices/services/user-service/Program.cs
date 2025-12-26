var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/", () => new
{
    Service = "User Service",
    Version = "1.0.0",
    Status = "Running"
});

app.MapGet("/api/users", () => new[]
{
    new { Id = 1, Name = "Alice", Email = "alice@example.com" },
    new { Id = 2, Name = "Bob", Email = "bob@example.com" },
    new { Id = 3, Name = "Charlie", Email = "charlie@example.com" }
});

app.MapGet("/api/users/{id:int}", (int id) =>
{
    var users = new Dictionary<int, object>
    {
        { 1, new { Id = 1, Name = "Alice", Email = "alice@example.com" } },
        { 2, new { Id = 2, Name = "Bob", Email = "bob@example.com" } },
        { 3, new { Id = 3, Name = "Charlie", Email = "charlie@example.com" } }
    };

    return users.TryGetValue(id, out var user)
        ? Results.Ok(user)
        : Results.NotFound(new { Error = $"User {id} not found" });
});

app.Run();

public partial class Program { }
