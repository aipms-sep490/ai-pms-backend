using System.Text.Json;
using AIPMS.Infrastructure.Services;

namespace AIPMS.UnitTests.AI;

public sealed class AssistantActionDraftParserTests
{
    [Fact]
    public void Empty_titles_do_not_produce_actionable_drafts()
    {
        using var doc = JsonDocument.Parse("""{"kind":"TASK","title":"   ","description":"Check API"}""");
        Assert.Null(AssistantActionDraftParser.Parse(doc.RootElement, "TASK"));
    }

    [Fact]
    public void Task_keeps_names_and_explicit_dates_but_never_accepts_entity_ids()
    {
        using var doc = JsonDocument.Parse("""{"kind":"TASK","title":"Test API","description":"Check access","task":{"dueAt":"2026-11-06T12:00:00+07:00","priority":"HIGH","milestoneTitle":"Testing","assigneeNames":["Hung","Hung",2],"primaryMajorName":"IT","assigneeUserIds":[999]}}""");
        var result = AssistantActionDraftParser.Parse(doc.RootElement, "TASK")!;
        Assert.Equal(new DateTimeOffset(2026,11,6,5,0,0,TimeSpan.Zero), result.Task!.DueAt);
        Assert.Equal("Hung", Assert.Single(result.Task.AssigneeNames));
        Assert.Equal("HIGH", result.Task.Priority);
        Assert.Null(AssistantActionDraftParser.Parse(doc.RootElement, "WEEKLY_REPORT"));
    }

    [Fact]
    public void Weekly_fields_are_separate_and_invalid_periods_are_not_invented()
    {
        using var doc = JsonDocument.Parse("""{"kind":"WEEKLY_REPORT","title":"Week","description":"Summary","report":{"periodStart":"2026-11-08","periodEnd":"2026-11-02","completedWork":"API done","blockers":"Waiting review"}}""");
        var result = AssistantActionDraftParser.Parse(doc.RootElement, "WEEKLY_REPORT")!;
        Assert.Null(result.Report!.PeriodStart); Assert.Null(result.Report.PeriodEnd);
        Assert.Equal("API done", result.Report.CompletedWork); Assert.Null(result.Report.Risks);
    }

    [Fact]
    public void Plan_accepts_only_bounded_task_children_without_recursing_into_plans()
    {
        using var doc = JsonDocument.Parse("""{"kind":"WORK_PLAN","title":"Plan","description":"Next steps","tasks":[{"kind":"TASK","title":"Test","description":"Check API","task":{"priority":"FAKE","dueAt":"tomorrow"}},{"kind":"MEETING","title":"Call","description":"Call"},null]}""");
        var result = AssistantActionDraftParser.Parse(doc.RootElement, "WORK_PLAN")!;
        var task = Assert.Single(result.Tasks!); Assert.Null(task.Task!.Priority); Assert.Null(task.Task.DueAt);
    }
}
