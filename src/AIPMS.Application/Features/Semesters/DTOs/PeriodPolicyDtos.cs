namespace AIPMS.Application.Features.Semesters.DTOs;

public sealed record PeriodPolicyFields(string AllowedProjectModes, string AllowedProposalSources,
    int MinTeamSize, int MaxTeamSize, int MinDistinctMajors, int MaxProjectsPerSupervisor);
public sealed record UpdatePeriodPolicyRequest(int ExpectedVersion, string Operation, PeriodPolicyFields Policy,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, string? ConcurrencyToken = null);
public sealed record PeriodPolicyDto(long Id, long ProjectPeriodId, int Version, string Status,
    DateTime EffectiveFrom, DateTime EffectiveTo, string ConcurrencyToken, PeriodPolicyFields Policy);
