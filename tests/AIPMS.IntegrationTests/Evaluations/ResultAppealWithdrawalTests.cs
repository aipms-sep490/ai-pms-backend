using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Appeals;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    [Fact]
    public async Task Pending_student_appeal_can_be_withdrawn_only_by_owner_with_current_token()
    {
        using var app = new AppealFactory(database);
        var (scenario, scheme, _, _) = await PublishedAppeal(app);
        using var staff = app.CreateAuthenticatedClient(scenario.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(scenario.Scope.Users.Student);
        using var outsider = app.CreateAuthenticatedClient(scenario.Scope.Users.OtherLecturer);
        var root = AppealUrl(scenario.ProjectId);
        var policy = await Body<AppealPolicyDto>(await staff.PostAsJsonAsync(root + "/appeal-policy",
            new RecordAppealPolicyRequest(0, "SYNTHETIC-WITHDRAW-POLICY")));
        var window = await Body<AppealWindowDto>(await staff.PostAsJsonAsync(root + "/appeal-windows",
            AppealWindow(policy.Id, scenario.Scope.Users.Student)));
        var input = new CreateCorrectionRequest("STUDENT_APPEAL", policy.Id, window.Id, "STUDENT",
            scenario.Scope.Users.Student, 1, scheme.Components.Single(c => c.Scope == "INDIVIDUAL").Id,
            "Please check the published result");
        var row = await Body<CorrectionRequestDto>(await student.PostAsJsonAsync(root + "/correction-requests", input));
        var url = root + $"/correction-requests/{row.Id}/withdraw";
        var request = new WithdrawAppealRequest(row.ConcurrencyToken, "I will provide a corrected report first.");

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(url,
            request with { ConcurrencyToken = Guid.NewGuid().ToString("N") })).StatusCode);
        var withdrawn = await Body<CorrectionRequestDto>(await student.PostAsJsonAsync(url, request));
        Assert.Equal("WITHDRAWN", withdrawn.Status);
        Assert.NotEqual(row.ConcurrencyToken, withdrawn.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(url, request)).StatusCode);
        var history = await Body<AIPMS.Application.Common.Models.PagedResult<AppealEventDto>>(
            await student.GetAsync(root + $"/correction-requests/{row.Id}/history"));
        Assert.Contains(history.Items, e => e.Action == "WITHDRAW_REQUEST" && e.Reason == request.Reason);
        await using var db = database.CreateContext();
        Assert.Equal("WITHDRAWN", (await db.Set<ResultAppealRequest>().FindAsync(row.Id))!.Status);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "WITHDRAW_REQUEST" && a.EntityId == row.Id.ToString()));
        var next = await Body<CorrectionRequestDto>(await student.PostAsJsonAsync(root + "/correction-requests", input));
        Assert.NotEqual(row.Id, next.Id);
        Assert.Equal("PENDING", next.Status);
    }
}
