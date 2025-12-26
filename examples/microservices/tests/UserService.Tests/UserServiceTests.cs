using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace UserService.Tests;

public class UserServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public UserServiceTests(WebApplicationFactory<Program> factory)
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
        Assert.Contains("User Service", content);
    }

    [Fact]
    public async Task GetUsers_ReturnsUserList()
    {
        var response = await _client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alice", content);
        Assert.Contains("Bob", content);
    }

    [Fact]
    public async Task GetUserById_ExistingUser_ReturnsUser()
    {
        var response = await _client.GetAsync("/api/users/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alice", content);
    }

    [Fact]
    public async Task GetUserById_NonExistingUser_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/users/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
