using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace WebApi.Tests;

public class WeatherTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WeatherTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetWeatherForecast_ReturnsSuccessStatusCode()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/weatherforecast");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherForecast_ReturnsJsonContent()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/weatherforecast");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("temperatureC", content.ToLower());
    }

    [Fact]
    public async Task GetWeatherForecast_ReturnsFiveDays()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/weatherforecast");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        // Should contain 5 forecast items
        var count = content.Split("\"date\"").Length - 1;
        Assert.Equal(5, count);
    }
}

// Required for WebApplicationFactory
public partial class Program { }
