namespace AIPMS.Application.Features.Teams.Models;

public sealed record StudentQualificationEligibility(
    bool Required,
    bool Eligible,
    string Status,
    string? IssueCode);
