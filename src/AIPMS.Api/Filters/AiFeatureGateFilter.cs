using System.Collections.Concurrent;
using System.Security.Claims;
using AIPMS.AI.Configuration;
using AIPMS.Api.Middleware;
using AIPMS.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AIPMS.Api.Filters;

public enum AiFeatureArea
{
    Suite,
    MeetingAi,
    ChatAi,
    Insights,
    RubricGradingCopilot,
    AppealReviewAssist,
    ContributionFairness,
    LegacyProjectAsk,
    SuggestEvaluationScheme
}

public sealed class AiDailyQuotaTracker
{
    private readonly ConcurrentDictionary<(string ActorKey, DateOnly UtcDate), int> counters = new();

    public bool TryConsume(string actorKey, DateTime utcNow, int limit, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (limit <= 0) return true;

        var today = DateOnly.FromDateTime(utcNow);
        if (counters.Count > 10_000)
        {
            foreach (var key in counters.Keys)
            {
                if (key.UtcDate < today) counters.TryRemove(key, out _);
            }
        }

        var count = counters.AddOrUpdate((actorKey, today), 1, static (_, current) => current + 1);
        if (count <= limit) return true;

        var nextMidnight = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((nextMidnight - utcNow).TotalSeconds));
        return false;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AiFeatureGateAttribute(AiFeatureArea area) : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new AiFeatureGateFilter(
            area,
            serviceProvider.GetRequiredService<IConfiguration>(),
            serviceProvider.GetRequiredService<AiDailyQuotaTracker>(),
            serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System);
}

internal sealed class AiFeatureGateFilter(
    AiFeatureArea area,
    IConfiguration configuration,
    AiDailyQuotaTracker quotaTracker,
    TimeProvider clock) : IAsyncActionFilter
{
    private const string QuotaConsumedItemKey = "AIPMS.AiDailyQuotaConsumed";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var opts = configuration.GetSection(AiAssistantOptions.SectionName).Get<AiAssistantOptions>()
            ?? new AiAssistantOptions();

        if (!opts.Enabled || !IsAreaEnabled(opts, area))
        {
            throw new ServiceUnavailableException(
                "This AI feature is disabled by system configuration.",
                WorkflowErrorCodes.AiFeatureDisabled);
        }

        // Only consume 1 quota unit per HTTP request even if both class-level and method-level attributes are present.
        if (opts.DailyQuotaPerUser > 0 && !context.HttpContext.Items.ContainsKey(QuotaConsumedItemKey))
        {
            context.HttpContext.Items[QuotaConsumedItemKey] = true;
            var actorKey = ResolveActorKey(context.HttpContext);
            if (!quotaTracker.TryConsume(actorKey, clock.GetUtcNow().UtcDateTime, opts.DailyQuotaPerUser, out var retryAfterSeconds))
            {
                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                var problem = new ProblemDetails
                {
                    Title = "AI daily quota exceeded.",
                    Detail = $"Daily AI request quota ({opts.DailyQuotaPerUser}/day) has been exceeded.",
                    Status = StatusCodes.Status429TooManyRequests,
                    Instance = context.HttpContext.Request.Path
                };
                problem.Extensions["code"] = WorkflowErrorCodes.AiDailyQuotaExceeded;
                problem.Extensions["errorCode"] = WorkflowErrorCodes.AiDailyQuotaExceeded;
                problem.Extensions["message"] = problem.Detail;
                problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                problem.Extensions["correlationId"] = ExceptionHandlingMiddleware.ResolveCorrelationId(context.HttpContext);
                problem.Extensions["retryable"] = true;
                context.Result = new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status429TooManyRequests
                };
                return;
            }
        }

        await next();
    }

    internal static bool IsAreaEnabled(AiAssistantOptions opts, AiFeatureArea area) => area switch
    {
        AiFeatureArea.Suite => opts.SuiteEnabled,
        AiFeatureArea.MeetingAi => opts.MeetingAiEnabled,
        AiFeatureArea.ChatAi => opts.ChatAiEnabled,
        AiFeatureArea.Insights => opts.InsightsEnabled,
        AiFeatureArea.RubricGradingCopilot => opts.SuiteEnabled && opts.RubricGradingCopilotEnabled,
        AiFeatureArea.AppealReviewAssist => opts.SuiteEnabled && opts.AppealReviewAssistEnabled,
        AiFeatureArea.ContributionFairness => opts.SuiteEnabled && opts.ContributionFairnessEnabled,
        AiFeatureArea.SuggestEvaluationScheme => opts.SuiteEnabled && opts.SuggestEvaluationSchemeEnabled,
        AiFeatureArea.LegacyProjectAsk => opts.LegacyProjectAskEnabled,
        _ => false
    };

    private static string ResolveActorKey(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(userId)) return $"user:{userId}";
        }
        return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
