using System.Text.Json;
using AIPMS.Api.Middleware;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIPMS.IntegrationTests.Api;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Cancelled_client_does_not_turn_an_aborted_query_into_a_server_error()
    {
        using var cancellation = new CancellationTokenSource();
        using var services = new ServiceCollection().AddOptions().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellation.Token };
        context.Request.Path = "/api/v1/rubrics";
        cancellation.Cancel();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new TaskCanceledException("Request cancelled by client."),
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            services.GetRequiredService<IProblemDetailsService>());

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task Additive_problem_details_include_v4_fields_and_preserve_legacy_keys()
    {
        var metadata = new ProblemMetadata(
            BlockingRequirements: [new BlockingRequirement("MISSING_SCORE", "Score is required.", "criterion:1")],
            CurrentState: "DRAFT",
            ExpectedState: "FINALIZED",
            PolicyVersion: "POL-2026-V4");

        var (status, doc) = await InvokeAsync(
            new DomainException("Scores are incomplete.", WorkflowErrorCodes.IncompleteScores, metadata),
            correlationHeader: "corr-p0-test-123");

        Assert.Equal(422, status);
        var root = doc.RootElement;
        Assert.Equal(WorkflowErrorCodes.IncompleteScores, root.GetProperty("code").GetString());
        Assert.Equal(WorkflowErrorCodes.IncompleteScores, root.GetProperty("errorCode").GetString());
        Assert.Equal("Scores are incomplete.", root.GetProperty("message").GetString());
        Assert.Equal("corr-p0-test-123", root.GetProperty("correlationId").GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
        Assert.Equal("DRAFT", root.GetProperty("currentState").GetString());
        Assert.Equal("FINALIZED", root.GetProperty("expectedState").GetString());
        Assert.Equal("POL-2026-V4", root.GetProperty("policyVersion").GetString());
        Assert.False(root.GetProperty("retryable").GetBoolean());
        Assert.Equal("MISSING_SCORE", root.GetProperty("blockingRequirements")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Forbidden_and_413_and_concurrency_envelopes_follow_security_and_compatibility_rules()
    {
        var leakyMetadata = new ProblemMetadata(
            FieldErrors: new Dictionary<string, string[]> { ["secretField"] = ["should-not-leak"] },
            BlockingRequirements: [new BlockingRequirement("HIDDEN", "secret")]);

        var (forbiddenStatus, forbiddenDoc) = await InvokeAsync(
            new ForbiddenException("Assignment revoked.", WorkflowErrorCodes.AssignmentRevoked, leakyMetadata));
        Assert.Equal(403, forbiddenStatus);
        Assert.Equal(WorkflowErrorCodes.AssignmentRevoked, forbiddenDoc.RootElement.GetProperty("code").GetString());
        Assert.False(forbiddenDoc.RootElement.TryGetProperty("fieldErrors", out _));
        Assert.False(forbiddenDoc.RootElement.TryGetProperty("blockingRequirements", out _));

        var (tooLargeStatus, tooLargeDoc) = await InvokeAsync(
            new PayloadTooLargeException(20 * 1024 * 1024, "Too large."));
        Assert.Equal(413, tooLargeStatus);
        Assert.Equal("UPLOAD_TOO_LARGE", tooLargeDoc.RootElement.GetProperty("code").GetString());
        Assert.Equal(20 * 1024 * 1024, tooLargeDoc.RootElement.GetProperty("maxBytes").GetInt64());

        var (concurrencyStatus, concurrencyDoc) = await InvokeAsync(new DbUpdateConcurrencyException("stale"));
        Assert.Equal(409, concurrencyStatus);
        Assert.Equal(WorkflowErrorCodes.StaleConcurrencyToken, concurrencyDoc.RootElement.GetProperty("code").GetString());
        Assert.True(concurrencyDoc.RootElement.GetProperty("retryable").GetBoolean());
    }

    private static async Task<(int Status, JsonDocument Document)> InvokeAsync(Exception exception, string? correlationHeader = null)
    {
        using var services = new ServiceCollection().AddOptions().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/v1/test";
        if (correlationHeader is not null) context.Request.Headers["X-Correlation-Id"] = correlationHeader;
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw exception,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            services.GetRequiredService<IProblemDetailsService>());

        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, doc);
    }
}
