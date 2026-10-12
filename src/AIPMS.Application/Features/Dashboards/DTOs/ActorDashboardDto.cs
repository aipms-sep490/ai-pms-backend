namespace AIPMS.Application.Features.Dashboards.DTOs;

public sealed record ActorDashboardDto(DateTime AsOfUtc, long ProjectCount, long OpenTasks, long OverdueTasks,
    long PendingEvaluations, long UnreadNotifications, long? ActiveAccounts, string Scope, bool Complete);
