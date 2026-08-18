using System.Net;
using System.Text.Json;
using Fulfillment.Application.Common.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fulfillment.IntegrationTests.ErrorHandlingTests;

public class TestErrorWebApplicationFactory : WebApplicationFactory<Program>
{
    public string TargetEnvironment { get; set; } = "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TargetEnvironment);

        builder.Configure(app =>
        {
            var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

            app.UseExceptionHandler();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/health", () => Results.Ok(new { status = "Healthy", environment = env.EnvironmentName }));
                endpoints.MapGet("/test-error/not-found", () => { throw new NotFoundException("Test item not found."); });
                endpoints.MapGet("/test-error/conflict", () => { throw new ConflictException("Test entity conflict."); });
                endpoints.MapGet("/test-error/validation", () => { throw new ValidationException("Test validation error."); });
                endpoints.MapGet("/test-error/unauthorized", () => { throw new UnauthorizedAccessException("Test unauthorized access."); });
                endpoints.MapGet("/test-error/forbidden", () => { throw new ForbiddenException("Test forbidden access."); });
                endpoints.MapGet("/test-error/unexpected", () => { throw new InvalidOperationException("Test unexpected database failure."); });
            });
        });
    }
}

public class GlobalErrorHandlingTests
{
    [Fact]
    public async Task NotFoundException_ReturnsProblemDetails_With404AndTraceId()
    {
        using var factory = new TestErrorWebApplicationFactory { TargetEnvironment = "Development" };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/test-error/not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal("Resource Not Found", root.GetProperty("title").GetString());
        Assert.Contains("Test item not found.", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("traceId", out var traceId) && !string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task ConflictException_ReturnsProblemDetails_With409AndTraceId()
    {
        using var factory = new TestErrorWebApplicationFactory { TargetEnvironment = "Development" };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/test-error/conflict");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal("Business Conflict", root.GetProperty("title").GetString());
        Assert.Contains("Test entity conflict.", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("traceId", out var traceId) && !string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task ValidationException_ReturnsProblemDetails_With400AndTraceId()
    {
        using var factory = new TestErrorWebApplicationFactory { TargetEnvironment = "Development" };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/test-error/validation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("Validation Error", root.GetProperty("title").GetString());
        Assert.Contains("Test validation error.", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("traceId", out var traceId) && !string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task DevelopmentEnvironment_UnexpectedException_ReturnsDetailedDeveloperResponse()
    {
        using var factory = new TestErrorWebApplicationFactory { TargetEnvironment = "Development" };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/test-error/unexpected");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());

        var detail = root.GetProperty("detail").GetString();
        Assert.NotNull(detail);
        Assert.Contains("System.InvalidOperationException", detail);
        Assert.Contains("Test unexpected database failure.", detail);

        Assert.True(root.TryGetProperty("traceId", out var traceId) && !string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task ProductionEnvironment_UnexpectedException_ReturnsClientSafeResponse_WithoutStackTraces()
    {
        using var factory = new TestErrorWebApplicationFactory { TargetEnvironment = "Production" };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/test-error/unexpected");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());

        var detail = root.GetProperty("detail").GetString();
        Assert.Equal("An error occurred while processing your request.", detail);

        // Verify no sensitive internal details are exposed
        Assert.DoesNotContain("InvalidOperationException", json);
        Assert.DoesNotContain("at ", json);

        Assert.True(root.TryGetProperty("traceId", out var traceId) && !string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task ProductionAPI_ExposesNoTestEndpoints_OrBusinessEndpoints()
    {
        using var defaultFactory = new WebApplicationFactory<Program>();
        var client = defaultFactory.CreateClient();

        var testEndpointResponse = await client.GetAsync("/test-error/unexpected");
        var productsResponse = await client.GetAsync("/api/products");
        var healthResponse = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.NotFound, testEndpointResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, productsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }
}
