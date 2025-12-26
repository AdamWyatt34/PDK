var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/", () => new
{
    Service = "Order Service",
    Version = "1.0.0",
    Status = "Running"
});

app.MapGet("/api/orders", () => new[]
{
    new { Id = 101, UserId = 1, Product = "Widget", Quantity = 2, Total = 29.99m },
    new { Id = 102, UserId = 2, Product = "Gadget", Quantity = 1, Total = 49.99m },
    new { Id = 103, UserId = 1, Product = "Gizmo", Quantity = 3, Total = 89.97m }
});

app.MapGet("/api/orders/{id:int}", (int id) =>
{
    var orders = new Dictionary<int, object>
    {
        { 101, new { Id = 101, UserId = 1, Product = "Widget", Quantity = 2, Total = 29.99m } },
        { 102, new { Id = 102, UserId = 2, Product = "Gadget", Quantity = 1, Total = 49.99m } },
        { 103, new { Id = 103, UserId = 1, Product = "Gizmo", Quantity = 3, Total = 89.97m } }
    };

    return orders.TryGetValue(id, out var order)
        ? Results.Ok(order)
        : Results.NotFound(new { Error = $"Order {id} not found" });
});

app.MapGet("/api/orders/user/{userId:int}", (int userId) =>
{
    var allOrders = new[]
    {
        new { Id = 101, UserId = 1, Product = "Widget", Quantity = 2, Total = 29.99m },
        new { Id = 102, UserId = 2, Product = "Gadget", Quantity = 1, Total = 49.99m },
        new { Id = 103, UserId = 1, Product = "Gizmo", Quantity = 3, Total = 89.97m }
    };

    var userOrders = allOrders.Where(o => o.UserId == userId).ToArray();
    return Results.Ok(userOrders);
});

app.Run();

public partial class Program { }
