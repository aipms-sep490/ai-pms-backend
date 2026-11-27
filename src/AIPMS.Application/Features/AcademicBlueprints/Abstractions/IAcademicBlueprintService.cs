using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.AcademicBlueprints.Abstractions;

public interface IAcademicBlueprintService
{
    Task<IReadOnlyList<AcademicBlueprintDto>> ListAsync(long? semesterId, string? trackCode, string? status, CancellationToken ct = default);
    Task<AcademicBlueprintDto> GetAsync(long id, CancellationToken ct = default);
    Task<AcademicBlueprintDto> CreateAsync(CreateAcademicBlueprintRequest request, CancellationToken ct = default);
    Task<AcademicBlueprintDto> UpdateAsync(long id, UpdateAcademicBlueprintRequest request, CancellationToken ct = default);
    Task<AcademicBlueprintDto> CloneVersionAsync(long id, CancellationToken ct = default);
    Task<AcademicBlueprintDto> SubmitForReviewAsync(long id, CancellationToken ct = default);
    Task<AcademicBlueprintDto> RecordApprovalAsync(long id, RecordBlueprintApprovalRequest request, CancellationToken ct = default);
    Task<AcademicBlueprintDto> PublishAsync(long id, CancellationToken ct = default);

    Task<SemesterReadinessDto> GetSemesterReadinessAsync(long semesterId, CancellationToken ct = default);
    Task EnsureSemesterBlueprintReadyForRegistrationAsync(long semesterId, CancellationToken ct = default);
    Task<IReadOnlyList<SemesterTrackPresetDto>> GetSemesterTrackPresetsAsync(long semesterId, CancellationToken ct = default);

    Task<BlueprintDryRunDiffDto> DryRunProjectPlanAsync(long projectId, long? blueprintId = null, CancellationToken ct = default);
    Task<ProjectBlueprintSnapshotDto?> ApplyBlueprintToProjectAsync(long projectId, long actorUserId, DateTime activatedAtUtc, long? blueprintId = null, CancellationToken ct = default);

    Task<IReadOnlyList<GateDeadlineChangeRequestDto>> ListDeadlineChangeRequestsAsync(long projectId, string? gateCode = null, CancellationToken ct = default);
    Task<GateDeadlineChangeRequestDto> CreateDeadlineChangeRequestAsync(long projectId, string gateCode, CreateGateDeadlineChangeRequest request, CancellationToken ct = default);
    Task<GateDeadlineChangeRequestDto> ReviewDeadlineChangeRequestAsync(long projectId, long requestId, ReviewGateDeadlineChangeRequest request, CancellationToken ct = default);
}
