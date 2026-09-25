using AIPMS.Application.Common.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Models;

public sealed record TeamRegistrationWindow(long PeriodId, long SemesterId,
    long OrganizationId, DateTime EndAt,
    string AllowedProjectModes = "SINGLE_MAJOR,INTERDISCIPLINARY",
    string AllowedProposalSources = "PUBLISHED_TOPIC,STUDENT_PROPOSAL",
    int PolicyVersion = 1);
