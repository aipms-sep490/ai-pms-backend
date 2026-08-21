using AIPMS.Application.Common.Exceptions;
using AIPMS.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IProblemDetailsService problemDetailsService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                logger.LogWarning(
                    exception,
                    "The response already started before an exception was handled for {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);
                throw;
            }

            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var mapping = MapException(exception);

        if (mapping.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "Request {Method} {Path} failed with {StatusCode} because of {ExceptionType}",
                context.Request.Method,
                context.Request.Path,
                mapping.StatusCode,
                exception.GetType().Name);
        }

        context.Response.StatusCode = mapping.StatusCode;

        var problem = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Type = mapping.Type,
            Detail = mapping.ExposeDetail
                ? exception.Message
                : "Use the trace id when contacting support.",
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (exception is ValidationException validationException)
        {
            problem.Extensions["errors"] = validationException.Errors;
        }

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        });
    }

    private static ExceptionMapping MapException(Exception exception) => exception switch
    {
        ValidationException => new(
            StatusCodes.Status400BadRequest,
            "Validation failed.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1"),
        ArgumentException => new(
            StatusCodes.Status400BadRequest,
            "The request is invalid.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.1"),
        UnauthorizedException => new(
            StatusCodes.Status401Unauthorized,
            "Authentication failed.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.2"),
        ForbiddenException => new(
            StatusCodes.Status403Forbidden,
            "Access is forbidden.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.4"),
        NotFoundException => new(
            StatusCodes.Status404NotFound,
            "The requested resource was not found.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.5"),
        ConflictException => new(
            StatusCodes.Status409Conflict,
            "The request conflicts with the current resource state.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.10"),
        DomainException => new(
            StatusCodes.Status422UnprocessableEntity,
            "A business rule was violated.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.21"),
        _ => new(
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.1",
            ExposeDetail: false)
    };

    private sealed record ExceptionMapping(
        int StatusCode,
        string Title,
        string Type,
        bool ExposeDetail = true);
}
