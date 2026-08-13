using AIPMS.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (exception is DomainException or ArgumentException)
        {
            await WriteProblemAsync(context, exception, StatusCodes.Status400BadRequest);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);
            await WriteProblemAsync(context, exception, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        Exception exception,
        int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode == StatusCodes.Status400BadRequest
                ? "The request violates a business rule."
                : "An unexpected error occurred.",
            Detail = statusCode == StatusCodes.Status400BadRequest
                ? exception.Message
                : "Use the trace id when contacting support.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        await context.Response.WriteAsJsonAsync(problem);
    }
}
