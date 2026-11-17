using AIPMS.Api.Middleware;
using Microsoft.AspNetCore.Http;
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
}
