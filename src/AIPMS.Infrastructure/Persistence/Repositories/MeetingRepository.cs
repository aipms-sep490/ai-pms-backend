using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class MeetingRepository(AipmsDbContext context) : IMeetingRepository
{
    public async Task<MeetingDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Meetings
            .AsNoTracking()
            .Include(m => m.CreatedByNavigation)
            .Include(m => m.MeetingParticipants)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return entity?.ToDto();
    }

    public async Task<MeetingDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await context.Meetings
            .AsNoTracking()
            .Include(m => m.CreatedByNavigation)
            .Include(m => m.MeetingParticipants)
                .ThenInclude(p => p.User)
            .Include(m => m.SupervisorFeedbacks)
                .ThenInclude(f => f.SupervisorAssignment)
                    .ThenInclude(a => a.SupervisorProfile)
                        .ThenInclude(sp => sp.User)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return entity?.ToDetailDto();
    }

    public async Task<PagedResult<MeetingDto>> GetMeetingsAsync(
        long projectId,
        string? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.Meetings
            .AsNoTracking()
            .Include(m => m.CreatedByNavigation)
            .Include(m => m.MeetingParticipants)
            .Where(m => m.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m => m.Status == status);
        }

        if (from.HasValue)
        {
            query = query.Where(m => m.StartAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(m => m.StartAt <= to.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.StartAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(m => m.ToDto()).ToList();
        return new PagedResult<MeetingDto>(dtos, page, pageSize, totalCount);
    }

    public Task<MeetingDto> CreateAsync(
        long projectId, long createdBy, string title, string? agenda, DateTime startAt, DateTime? endAt,
        string? location, string? onlineUrl, IReadOnlyList<long>? participantUserIds, DateTime now, CancellationToken cancellationToken) =>
        CreateAsync(projectId, createdBy, title, agenda, startAt, endAt, location, onlineUrl, participantUserIds, now, onCreated: null, cancellationToken);

    public Task<MeetingDto> UpdateAsync(
        long id, string title, string? agenda, DateTime startAt, DateTime? endAt, string? location, string? onlineUrl, DateTime now, CancellationToken cancellationToken) =>
        UpdateAsync(id, title, agenda, startAt, endAt, location, onlineUrl, now, onUpdated: null, cancellationToken);

    public Task<MeetingDto> CancelAsync(long id, DateTime now, CancellationToken cancellationToken) =>
        CancelAsync(id, now, onCancelled: null, cancellationToken);

    public Task<MeetingDto> CompleteAsync(long id, DateTime now, CancellationToken cancellationToken) =>
        CompleteAsync(id, now, onCompleted: null, cancellationToken);

    public Task<MeetingDto> UpdateNotesAsync(
        long id, string? meetingNotes, IReadOnlyList<ParticipantAttendanceUpdate>? attendances, DateTime now, CancellationToken cancellationToken) =>
        UpdateNotesAsync(id, meetingNotes, attendances, now, onNotesUpdated: null, cancellationToken);

    public Task<MeetingParticipantDto> AddParticipantAsync(
        long meetingId, long userId, string? attendanceStatus, DateTime now, CancellationToken cancellationToken) =>
        AddParticipantAsync(meetingId, userId, attendanceStatus, now, onAdded: null, cancellationToken);

    public Task RemoveParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken) =>
        RemoveParticipantAsync(meetingId, userId, onRemoved: null, cancellationToken);

    public Task<MeetingFeedbackDto> AddFeedbackAsync(
        long meetingId, long supervisorAssignmentId, string feedbackText, DateTime now, CancellationToken cancellationToken) =>
        AddFeedbackAsync(meetingId, supervisorAssignmentId, feedbackText, now, onAdded: null, cancellationToken);

    public async Task<MeetingDto> CreateAsync(
        long projectId,
        long createdBy,
        string title,
        string? agenda,
        DateTime startAt,
        DateTime? endAt,
        string? location,
        string? onlineUrl,
        IReadOnlyList<long>? participantUserIds,
        DateTime now,
        Func<MeetingDto, Task>? onCreated = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = new Meeting
        {
            ProjectId = projectId,
            Title = title,
            Agenda = agenda,
            StartAt = startAt,
            EndAt = endAt,
            Location = location,
            OnlineUrl = onlineUrl,
            Status = "SCHEDULED",
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Meetings.Add(meeting);

        var allParticipants = new HashSet<long> { createdBy };
        if (participantUserIds != null)
        {
            foreach (var uid in participantUserIds)
            {
                allParticipants.Add(uid);
            }
        }

        foreach (var uid in allParticipants)
        {
            meeting.MeetingParticipants.Add(new MeetingParticipant
            {
                UserId = uid,
                AttendanceStatus = uid == createdBy ? "ACCEPTED" : "INVITED",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        var result = (await GetByIdAsync(meeting.Id, cancellationToken))!;

        if (onCreated != null)
        {
            await onCreated(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<MeetingDto> UpdateAsync(
        long id,
        string title,
        string? agenda,
        DateTime startAt,
        DateTime? endAt,
        string? location,
        string? onlineUrl,
        DateTime now,
        Func<MeetingDto, Task>? onUpdated = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = await context.Meetings
            .FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK, ROWLOCK) WHERE id = {id}")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
            throw new NotFoundException("Meeting", id);

        if (meeting.Status is "CANCELLED" or "COMPLETED")
            throw new ConflictException($"Meeting cannot be updated because its status is {meeting.Status}.");

        meeting.Title = title;
        meeting.Agenda = agenda;
        meeting.StartAt = startAt;
        meeting.EndAt = endAt;
        meeting.Location = location;
        meeting.OnlineUrl = onlineUrl;
        meeting.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        var result = (await GetByIdAsync(id, cancellationToken))!;

        if (onUpdated != null)
        {
            await onUpdated(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<MeetingDto> CancelAsync(
        long id,
        DateTime now,
        Func<MeetingDto, Task>? onCancelled = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = await context.Meetings
            .FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK, ROWLOCK) WHERE id = {id}")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
            throw new NotFoundException("Meeting", id);

        if (meeting.Status is "COMPLETED" or "CANCELLED")
            throw new ConflictException($"Cannot cancel a meeting that is already {meeting.Status.ToLowerInvariant()}.");

        meeting.Status = "CANCELLED";
        meeting.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        var result = (await GetByIdAsync(id, cancellationToken))!;

        if (onCancelled != null)
        {
            await onCancelled(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<MeetingDto> CompleteAsync(
        long id,
        DateTime now,
        Func<MeetingDto, Task>? onCompleted = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = await context.Meetings
            .FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK, ROWLOCK) WHERE id = {id}")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
            throw new NotFoundException("Meeting", id);

        if (meeting.Status == "COMPLETED")
            throw new ConflictException("Meeting is already completed.");

        if (meeting.Status == "CANCELLED")
            throw new ConflictException("Cannot complete a meeting that has been cancelled.");

        if (meeting.Status != "SCHEDULED")
            throw new ConflictException($"Meeting with status '{meeting.Status}' cannot be completed.");

        meeting.Status = "COMPLETED";
        meeting.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        var result = (await GetByIdAsync(id, cancellationToken))!;

        if (onCompleted != null)
        {
            await onCompleted(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<MeetingDto> UpdateNotesAsync(
        long id,
        string? meetingNotes,
        IReadOnlyList<ParticipantAttendanceUpdate>? attendances,
        DateTime now,
        Func<MeetingDto, Task>? onNotesUpdated = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = await context.Meetings
            .FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK, ROWLOCK) WHERE id = {id}")
            .Include(m => m.MeetingParticipants)
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
            throw new NotFoundException("Meeting", id);

        if (meeting.Status == "CANCELLED")
            throw new ConflictException("Cancelled meetings cannot be modified.");

        if (meetingNotes != null)
        {
            meeting.MeetingNotes = meetingNotes;
        }

        // Notes endpoint does not mutate lifecycle status; terminal states cannot reopen

        if (attendances != null)
        {
            foreach (var att in attendances)
            {
                var participant = meeting.MeetingParticipants.FirstOrDefault(p => p.UserId == att.UserId);
                if (participant == null)
                    throw new ConflictException($"User {att.UserId} is not a participant of meeting {id}.");

                participant.AttendanceStatus = att.AttendanceStatus;
                participant.UpdatedAt = now;
            }
        }

        meeting.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        var result = (await GetByIdAsync(id, cancellationToken))!;

        if (onNotesUpdated != null)
        {
            await onNotesUpdated(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<MeetingParticipantDto> AddParticipantAsync(
        long meetingId,
        long userId,
        string? attendanceStatus,
        DateTime now,
        Func<MeetingParticipantDto, Task>? onAdded = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = await context.Meetings
            .FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK, ROWLOCK) WHERE id = {meetingId}")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
            throw new NotFoundException("Meeting", meetingId);

        if (meeting.Status is "CANCELLED" or "COMPLETED")
            throw new ConflictException($"Cannot add participants to a {meeting.Status.ToLowerInvariant()} meeting.");

        var participant = new MeetingParticipant
        {
            MeetingId = meetingId,
            UserId = userId,
            AttendanceStatus = attendanceStatus ?? "INVITED",
            CreatedAt = now,
            UpdatedAt = now
        };

        context.MeetingParticipants.Add(participant);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("User is already a participant in this meeting.");
        }

        var reloaded = await context.MeetingParticipants
            .AsNoTracking()
            .Include(p => p.User)
            .FirstAsync(p => p.Id == participant.Id, cancellationToken);

        var result = reloaded.ToDto();

        if (onAdded != null)
        {
            await onAdded(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task RemoveParticipantAsync(
        long meetingId,
        long userId,
        Func<Task>? onRemoved = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = await context.Meetings
            .FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK, ROWLOCK) WHERE id = {meetingId}")
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
            throw new NotFoundException("Meeting", meetingId);

        if (meeting.Status is "CANCELLED" or "COMPLETED")
            throw new ConflictException($"Cannot remove participants from a {meeting.Status.ToLowerInvariant()} meeting.");

        var participant = await context.MeetingParticipants
            .FirstOrDefaultAsync(p => p.MeetingId == meetingId && p.UserId == userId, cancellationToken);

        if (participant != null)
        {
            context.MeetingParticipants.Remove(participant);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (onRemoved != null)
        {
            await onRemoved();
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }
    }

    public async Task<MeetingFeedbackDto> AddFeedbackAsync(
        long meetingId,
        long supervisorAssignmentId,
        string feedbackText,
        DateTime now,
        Func<MeetingFeedbackDto, Task>? onAdded = null,
        CancellationToken cancellationToken = default)
    {
        await using var tx = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var meeting = await context.Meetings
            .FirstAsync(m => m.Id == meetingId, cancellationToken);

        var feedback = new SupervisorFeedback
        {
            ProjectId = meeting.ProjectId,
            SupervisorAssignmentId = supervisorAssignmentId,
            MeetingId = meetingId,
            FeedbackText = feedbackText,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.SupervisorFeedbacks.Add(feedback);
        meeting.UpdatedAt = now;

        await context.SaveChangesAsync(cancellationToken);

        var reloaded = await context.SupervisorFeedbacks
            .AsNoTracking()
            .Include(f => f.SupervisorAssignment)
                .ThenInclude(a => a.SupervisorProfile)
                    .ThenInclude(p => p.User)
            .FirstAsync(f => f.Id == feedback.Id, cancellationToken);

        var result = reloaded.ToMeetingFeedbackDto();

        if (onAdded != null)
        {
            await onAdded(result);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        return result;
    }

    public async Task<long?> GetProjectIdAsync(long meetingId, CancellationToken cancellationToken)
    {
        return await context.Meetings
            .AsNoTracking()
            .Where(m => m.Id == meetingId)
            .Select(m => (long?)m.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetStatusAsync(long meetingId, CancellationToken cancellationToken)
    {
        return await context.Meetings
            .AsNoTracking()
            .Where(m => m.Id == meetingId)
            .Select(m => m.Status)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken)
    {
        return await context.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == projectId, cancellationToken);
    }

    public async Task<bool> IsProjectMemberOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .AnyAsync(p => p.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null)
                || (p.SupervisorAssignment != null
                    && p.SupervisorAssignment.EndedAt == null
                    && p.SupervisorAssignment.SupervisorProfile.UserId == userId),
                cancellationToken);
    }

    public async Task<bool> CanScheduleMeetingAsync(long projectId, long userId, CancellationToken cancellationToken)
    {
        return await context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .AnyAsync(p => p.Team.TeamMembers.Any(m => m.UserId == userId && m.IsLeader && m.LeftAt == null)
                || (p.SupervisorAssignment != null
                    && p.SupervisorAssignment.EndedAt == null
                    && p.SupervisorAssignment.SupervisorProfile.UserId == userId),
                cancellationToken);
    }

    public async Task<bool> CanManageMeetingAsync(long meetingId, long userId, CancellationToken cancellationToken)
    {
        return await context.Meetings
            .AsNoTracking()
            .Where(m => m.Id == meetingId)
            .AnyAsync(m => m.CreatedBy == userId
                || m.Project.Team.TeamMembers.Any(tm => tm.UserId == userId && tm.IsLeader && tm.LeftAt == null)
                || (m.Project.SupervisorAssignment != null
                    && m.Project.SupervisorAssignment.EndedAt == null
                    && m.Project.SupervisorAssignment.SupervisorProfile.UserId == userId),
                cancellationToken);
    }

    public async Task<bool> IsParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken)
    {
        return await context.MeetingParticipants
            .AsNoTracking()
            .AnyAsync(p => p.MeetingId == meetingId && p.UserId == userId, cancellationToken);
    }

    public async Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken)
    {
        return await context.SupervisorAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId
                && a.EndedAt == null
                && a.SupervisorProfile.UserId == supervisorUserId)
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
