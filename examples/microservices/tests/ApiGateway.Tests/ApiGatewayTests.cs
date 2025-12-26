using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace ApiGateway.Tests;

public class ApiGatewayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiGatewayTests(WebApplicationFactory<Program> factory)
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
        Assert.Contains("API Gateway", content);
    }

    [Fact]
    public async Task StatusEndpoint_ReturnsEndpointList()
    {
        var response = await _client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("gateway", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("endpoints", content, StringComparison.OrdinalIgnoreCase);
    }
}
