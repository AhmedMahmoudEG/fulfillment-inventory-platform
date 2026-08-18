using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fulfillment.IntegrationTests.Swagger;

[Collection("IntegrationTests")]
public class SwaggerEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SwaggerEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningKey", "TestSigningKeyAtLeast256BitsLongForSecurity12345!");
            builder.UseSetting("Environment", "Development");
        });
    }

    [Fact]
    public async Task SwaggerJsonEndpoint_InDevelopment_Returns200OKWithJsonContent()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jsonContent = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"openapi\"", jsonContent);
        Assert.Contains("\"Fulfillment & Inventory Management API\"", jsonContent);
    }

    [Fact]
    public async Task SwaggerJsonEndpoint_ContainsJwtBearerSecurityDefinition_HttpTypeBearerScheme()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jsonContent = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;

        // Navigate components -> securitySchemes -> Bearer
        var components = root.GetProperty("components");
        var securitySchemes = components.GetProperty("securitySchemes");
        var bearerScheme = securitySchemes.GetProperty("Bearer");

        Assert.Equal("http", bearerScheme.GetProperty("type").GetString());
        Assert.Equal("bearer", bearerScheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearerScheme.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public async Task SwaggerUiEndpoint_InDevelopment_Returns200OKWithHtmlContent()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var htmlContent = await response.Content.ReadAsStringAsync();

        Assert.Contains("swagger", htmlContent, StringComparison.OrdinalIgnoreCase);
    }
}
