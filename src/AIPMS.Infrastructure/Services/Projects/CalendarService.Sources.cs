using System.Text.Json;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class CalendarService
{
    private IQueryable<CalendarRow> Source(int source, AccessScope scope)
    {
        var whole = scope.WholeProjects; var projects = scope.Projects; var majors = scope.MajorKeys; var actor = scope.Actor;
        switch (source)
        {
            case 0: return db.Tasks.AsNoTracking().Where(t => t.DueAt.HasValue && (whole.Contains(t.Milestone.ProjectId)
                || db.TaskDisciplines.Any(d => d.TaskId == t.Id && majors.Contains(t.Milestone.ProjectId.ToString() + ":" + d.MajorId.ToString()))))
                .Select(t => new CalendarRow { SourceOrder = 0, SourceType = "TASK", SourceId = t.Id, ProjectId = t.Milestone.ProjectId,
                    ProjectCode = t.Milestone.Project.Code, ProjectTitle = t.Milestone.Project.Title, Title = t.Title, StartAt = t.StartAt,
                    EndAt = t.DueAt, DueAt = t.DueAt!.Value, Status = t.Status, DeepLink = $"/projects/{t.Milestone.ProjectId}/tasks/{t.Id}" });
            case 1: return db.Database.SqlQuery<CalendarRow>($"""
                SELECT 1 SourceOrder, 'MILESTONE' SourceType, m.id SourceId, m.project_id ProjectId, p.code ProjectCode,
                  p.title ProjectTitle, m.title Title, CAST(m.start_date AS datetime2) StartAt, CAST(NULL AS datetime2) EndAt,
                  CAST(m.due_date AS datetime2) DueAt, m.status Status,
                  CONCAT('/projects/',p.id,'/milestones/',m.id) DeepLink
                FROM dbo.milestones m JOIN dbo.projects p ON p.id=m.project_id
                WHERE m.due_date IS NOT NULL AND m.project_id IN (SELECT TRY_CONVERT(bigint,value) FROM OPENJSON({JsonSerializer.Serialize(whole)}))
                """);
            case 2: return db.Meetings.AsNoTracking().Where(m => whole.Contains(m.ProjectId)
                || scope.Lecturer && projects.Contains(m.ProjectId) && m.MeetingParticipants.Any(p => p.UserId == actor)
                    && (db.SupervisorAssignments.Any(a => a.ProjectId == m.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == actor)
                        || db.Set<EvaluationAssignment>().Any(a => a.ProjectId == m.ProjectId && a.Status == "ACTIVE" && a.EvaluatorId == actor)))
                .Select(m => new CalendarRow { SourceOrder = 2, SourceType = "MEETING", SourceId = m.Id, ProjectId = m.ProjectId,
                    ProjectCode = m.Project.Code, ProjectTitle = m.Project.Title, Title = m.Title, StartAt = m.StartAt,
                    EndAt = m.EndAt, DueAt = m.StartAt, Status = m.Status, DeepLink = $"/projects/{m.ProjectId}/meetings/{m.Id}" });
            case 3: return db.Deliverables.AsNoTracking().Where(d => d.DueAt.HasValue && (whole.Contains(d.ProjectId)
                || d.TargetMajorId.HasValue && majors.Contains(d.ProjectId.ToString() + ":" + d.TargetMajorId.Value.ToString())))
                .Select(d => new CalendarRow { SourceOrder = 3, SourceType = "DELIVERABLE", SourceId = d.Id, ProjectId = d.ProjectId,
                    ProjectCode = d.Project.Code, ProjectTitle = d.Project.Title, Title = d.Title,
                    DueAt = d.DueAt!.Value, Status = d.Status, DeepLink = $"/projects/{d.ProjectId}/deliverables/{d.Id}" });
            case 4: return db.Set<FinalSubmission>().AsNoTracking().Join(db.Projects, f => f.ProjectId, p => p.Id, (f, p) => new { f, p })
                .Where(x => whole.Contains(x.f.ProjectId)).Select(x => new CalendarRow { SourceOrder = 4, SourceType = "FINAL_SUBMISSION", SourceId = x.f.Id,
                    ProjectId = x.p.Id, ProjectCode = x.p.Code, ProjectTitle = x.p.Title, Title = "Final submission", DueAt = x.f.Deadline,
                    Status = "LOCKED", DeepLink = $"/projects/{x.p.Id}/final-submission" });
            case 5: return db.Set<EvaluationAssignment>().AsNoTracking().Join(db.Projects, a => a.ProjectId, p => p.Id, (a, p) => new { a, p })
                .Join(db.ProjectPeriods, x => x.a.ProjectPeriodId, period => period.Id, (x, period) => new { x.a, x.p, period })
                .Where(x => scope.Lecturer && projects.Contains(x.p.Id) && x.a.EvaluatorId == actor && x.a.Status == "ACTIVE"
                    && x.a.DepartmentId == scope.DepartmentId && x.a.ScopeSnapshotJson != null && x.a.ComponentId.HasValue
                    && (x.a.Scope == "COMMON" && x.a.MajorId == null && x.a.StudentId == null
                        || x.a.Scope == "MAJOR_SPECIFIC" && x.a.MajorId != null && x.a.StudentId == null
                        || x.a.Scope == "INDIVIDUAL" && x.a.MajorId != null && x.a.StudentId != null)
                    && db.Set<EvaluationSchemeComponent>().Any(c => c.Id == x.a.ComponentId && c.Scope == x.a.Scope && c.MajorId == x.a.MajorId
                        && c.RubricId == x.a.RubricId && c.Stage == x.a.Stage
                        && db.Set<EvaluationScheme>().Any(s => s.Id == c.SchemeId && s.ProjectId == x.p.Id && s.Status == "PUBLISHED"
                            && s.ProjectPeriodId == x.a.ProjectPeriodId && s.PolicyVersionId == x.a.PolicyVersionId)))
                .Select(x => new CalendarRow { SourceOrder = 5, SourceType = "EVALUATION_ASSIGNMENT", SourceId = x.a.Id, ProjectId = x.p.Id,
                    ProjectCode = x.p.Code, ProjectTitle = x.p.Title, Title = "Assigned evaluation window", StartAt = x.period.StartAt,
                    EndAt = x.period.EndAt, DueAt = x.period.EndAt, Status = x.a.Status, DeepLink = $"/evaluation-assignments/{x.a.Id}" });
            case 6: return db.Set<ProjectCheckpoint>().AsNoTracking().Join(db.Projects, c => c.ProjectId, p => p.Id, (c, p) => new { c, p })
                .Where(x => scope.CheckpointProjects.Contains(x.p.Id) || x.c.MajorId == null && scope.StudentProjects.Contains(x.p.Id)
                    || x.c.MajorId.HasValue && (majors.Contains(x.p.Id.ToString() + ":" + x.c.MajorId.Value.ToString())
                        || scope.StudentMajorKeys.Contains(x.p.Id.ToString() + ":" + x.c.MajorId.Value.ToString())))
                .Select(x => new CalendarRow { SourceOrder = 6, SourceType = "CHECKPOINT", SourceId = x.c.Id, ProjectId = x.p.Id,
                    ProjectCode = x.p.Code, ProjectTitle = x.p.Title, Title = x.c.Title, DueAt = x.c.PlannedAt, Status = "TRACKING" });
            case 7: return db.Set<DefenseSession>().AsNoTracking().Join(db.Projects, s => s.ProjectId, p => p.Id, (s, p) => new { s, p })
                .Where(x => whole.Contains(x.p.Id) || scope.Lecturer && projects.Contains(x.p.Id)
                    && db.Set<DefenseCommitteeMember>().Any(m => m.CommitteeId == x.s.CommitteeId && m.UserId == actor))
                .Select(x => new CalendarRow { SourceOrder = 7, SourceType = "DEFENSE_SESSION", SourceId = x.s.Id, ProjectId = x.p.Id,
                    ProjectCode = x.p.Code, ProjectTitle = x.p.Title, Title = "Defense session", StartAt = x.s.StartAt,
                    EndAt = x.s.EndAt, DueAt = x.s.StartAt, Status = x.s.Status, DeepLink = $"/defense-sessions/{x.s.Id}" });
            case 8: return db.Set<ProgressReportPeriod>().AsNoTracking().Where(r => r.Deadline >= r.PeriodStart &&
                !db.ProgressReports.Any(report => report.ProgressReportPeriodId == r.Id && report.Status != "DRAFT") &&
                (whole.Contains(r.ProjectId) || scope.Lecturer && projects.Contains(r.ProjectId) &&
                    db.SupervisorAssignments.Any(a => a.ProjectId == r.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == actor)))
                .Select(r => new CalendarRow { SourceOrder = 8, SourceType = "PROGRESS_REPORT", SourceId = r.Id,
                    ProjectId = r.ProjectId, ProjectCode = r.Project.Code, ProjectTitle = r.Project.Title,
                    Title = $"Progress report {r.ReportType}", StartAt = r.PeriodStart, EndAt = r.Deadline,
                    DueAt = r.Deadline, Status = "OPEN", DeepLink = $"/projects/{r.ProjectId}/progress-reports" });
            default: throw new InvalidOperationException();
        }
    }
}
