using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace OrderService.Tests;

public class OrderServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OrderServiceTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RootEndpoint_ReturnsServiceInfo()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Order Service", content);
    }

    [Fact]
    public async Task GetOrders_ReturnsOrderList()
    {
        var response = await _client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Widget", content);
        Assert.Contains("Gadget", content);
    }

    [Fact]
    public async Task GetOrderById_ExistingOrder_ReturnsOrder()
    {
        var response = await _client.GetAsync("/api/orders/101");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Widget", content);
    }

    [Fact]
    public async Task GetOrderById_NonExistingOrder_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/orders/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrdersByUser_ReturnsUserOrders()
    {
        var response = await _client.GetAsync("/api/orders/user/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Widget", content);
        Assert.Contains("Gizmo", content);
    }
}
