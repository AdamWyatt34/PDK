var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/", () => new
{
    Service = "API Gateway",
    Version = "1.0.0",
    Status = "Running"
});

app.MapGet("/api/status", () => new
{
    Gateway = "Healthy",
    Timestamp = DateTime.UtcNow,
    Endpoints = new[]
    {
        "/api/users - User Service",
        "/api/orders - Order Service"
    }
});

app.Run();

public partial class Program { }
