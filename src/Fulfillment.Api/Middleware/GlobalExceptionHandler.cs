using System.Diagnostics;
using System.Text.Json;
using Fulfillment.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Api.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IHostEnvironment env, ILogger<GlobalExceptionHandler> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

        var (statusCode, title, typeUri) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation Error", "https://tools.ietf.org/html/rfc9110#section-15.5.1"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", "https://tools.ietf.org/html/rfc9110#section-15.5.2"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", "https://tools.ietf.org/html/rfc9110#section-15.5.4"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found", "https://tools.ietf.org/html/rfc9110#section-15.5.5"),
            ConflictException => (StatusCodes.Status409Conflict, "Business Conflict", "https://tools.ietf.org/html/rfc9110#section-15.5.10"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "https://tools.ietf.org/html/rfc9110#section-15.6.1")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "An unhandled exception occurred. TraceId: {TraceId}, Method: {Method}, Path: {Path}",
                traceId,
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        var detail = _env.IsDevelopment()
            ? $"{exception.GetType().FullName}: {exception.Message}{GetInnerExceptionMessage(exception)}\n{exception.StackTrace}"
            : (statusCode == StatusCodes.Status500InternalServerError ? "An error occurred while processing your request." : exception.Message);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = typeUri,
            Detail = detail
        };

        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: (JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }

    private static string GetInnerExceptionMessage(Exception ex)
    {
        return ex.InnerException != null ? $" ---> {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}" : string.Empty;
    }
}
