using AIPMS.Application.Common.Exceptions;
using AIPMS.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            if (context.RequestAborted.IsCancellationRequested)
            {
                // No problem response can be delivered after the client disconnects.
                if (!context.Response.HasStarted) context.Response.StatusCode = 499;
                logger.LogDebug("Client disconnected during {Method} {Path}: {ExceptionType}",
                    context.Request.Method, context.Request.Path, exception.GetType().Name);
                return;
            }

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
                "Request {Method} {Path} failed with {StatusCode} because of {ExceptionType}: {Message}",
                context.Request.Method,
                context.Request.Path,
                mapping.StatusCode,
                exception.GetType().Name,
                exception.Message);
        }

        context.Response.StatusCode = mapping.StatusCode;

        var detail = mapping.ExposeDetail
            ? exception.Message
            : "Use the trace id when contacting support.";

        var problem = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Type = mapping.Type,
            Detail = detail,
            Instance = context.Request.Path
        };

        var correlationId = ResolveCorrelationId(context);
        problem.Extensions["traceId"] = context.TraceIdentifier;
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["message"] = detail;

        var (explicitCode, metadata) = ExtractMetadata(exception);
        var resolvedCode = explicitCode ?? ResolveDefaultCode(exception, mapping.StatusCode);
        if (!string.IsNullOrWhiteSpace(resolvedCode))
        {
            problem.Extensions["code"] = resolvedCode;
            problem.Extensions["errorCode"] = resolvedCode;
        }

        var defaultRetryable = mapping.StatusCode is StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable
            || resolvedCode == WorkflowErrorCodes.StaleConcurrencyToken;
        problem.Extensions["retryable"] = metadata?.Retryable ?? defaultRetryable;

        // Never expose fieldErrors or blockingRequirements on 401/403/404 to avoid leaking out-of-scope data.
        var allowStructuredDetails = mapping.StatusCode is not (StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden
            or StatusCodes.Status404NotFound);

        if (exception is ValidationException validationException)
        {
            problem.Extensions["errors"] = validationException.Errors;
            if (allowStructuredDetails)
            {
                problem.Extensions["fieldErrors"] = validationException.Errors;
            }
        }
        else if (allowStructuredDetails && metadata?.FieldErrors is { Count: > 0 } fieldErrors)
        {
            problem.Extensions["fieldErrors"] = fieldErrors;
        }

        if (allowStructuredDetails && metadata is not null)
        {
            if (metadata.BlockingRequirements is { Count: > 0 } blockers)
            {
                problem.Extensions["blockingRequirements"] = blockers;
            }
            if (!string.IsNullOrWhiteSpace(metadata.CurrentState))
            {
                problem.Extensions["currentState"] = metadata.CurrentState;
            }
            if (!string.IsNullOrWhiteSpace(metadata.ExpectedState))
            {
                problem.Extensions["expectedState"] = metadata.ExpectedState;
            }
            if (!string.IsNullOrWhiteSpace(metadata.PolicyVersion))
            {
                problem.Extensions["policyVersion"] = metadata.PolicyVersion;
            }
        }

        if (mapping.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            problem.Extensions["code"] = "UPLOAD_TOO_LARGE";
            problem.Extensions["errorCode"] = "UPLOAD_TOO_LARGE";

            if (exception is PayloadTooLargeException payloadTooLarge)
            {
                problem.Extensions["maxBytes"] = payloadTooLarge.MaxBytes;
            }
            else
            {
                var endpoint = context.GetEndpoint();
                var limitMeta = endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>();
                if (limitMeta?.MaxRequestBodySize is long limit)
                {
                    problem.Extensions["maxBytes"] = limit;
                }
                else
                {
                    var maxBodySize = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
                    if (maxBodySize.HasValue)
                    {
                        problem.Extensions["maxBytes"] = maxBodySize.Value;
                    }
                }
            }
        }

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        });
    }

    internal static string ResolveCorrelationId(HttpContext context)
    {
        var header = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()?.Trim();
        return !string.IsNullOrWhiteSpace(header) && header.Length <= 128
            ? header
            : context.TraceIdentifier;
    }

    private static (string? Code, ProblemMetadata? Metadata) ExtractMetadata(Exception exception) => exception switch
    {
        ConflictException conflict => (conflict.Code, conflict.Metadata),
        ForbiddenException forbidden => (forbidden.Code, forbidden.Metadata),
        ServiceUnavailableException unavailable => (unavailable.Code, unavailable.Metadata),
        DomainException domain => (domain.Code, domain.Metadata),
        NotFoundException notFound => (notFound.ErrorCode, null),
        _ => (null, null)
    };

    private static string? ResolveDefaultCode(Exception exception, int statusCode) => exception switch
    {
        DbUpdateConcurrencyException => WorkflowErrorCodes.StaleConcurrencyToken,
        ValidationException => WorkflowErrorCodes.ValidationFailed,
        UnauthorizedException => WorkflowErrorCodes.Unauthorized,
        ForbiddenException => WorkflowErrorCodes.Forbidden,
        NotFoundException => WorkflowErrorCodes.NotFound,
        _ => statusCode switch
        {
            StatusCodes.Status401Unauthorized => WorkflowErrorCodes.Unauthorized,
            StatusCodes.Status403Forbidden => WorkflowErrorCodes.Forbidden,
            StatusCodes.Status404NotFound => WorkflowErrorCodes.NotFound,
            StatusCodes.Status429TooManyRequests => WorkflowErrorCodes.RateLimited,
            StatusCodes.Status500InternalServerError => WorkflowErrorCodes.InternalServerError,
            _ => null
        }
    };

    private static ExceptionMapping MapException(Exception exception) => exception switch
    {
        PayloadTooLargeException => new(
            StatusCodes.Status413PayloadTooLarge,
            "Request entity too large.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.14"),
        BadHttpRequestException badHttp when badHttp.StatusCode == StatusCodes.Status413PayloadTooLarge => new(
            StatusCodes.Status413PayloadTooLarge,
            "Request entity too large.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.14"),
        InvalidDataException invalidData when invalidData.Message.Contains("Multipart body length limit", StringComparison.OrdinalIgnoreCase) => new(
            StatusCodes.Status413PayloadTooLarge,
            "Request entity too large.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.5.14"),
        ServiceUnavailableException => new(
            StatusCodes.Status503ServiceUnavailable,
            "Service temporarily unavailable.",
            "https://www.rfc-editor.org/rfc/rfc9110#section-15.6.4"),
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
        ConflictException or DbUpdateConcurrencyException => new(
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
