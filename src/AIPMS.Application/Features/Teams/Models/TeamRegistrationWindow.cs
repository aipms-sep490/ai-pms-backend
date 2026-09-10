using AIPMS.Application.Common.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Models;

public sealed record TeamRegistrationWindow(long PeriodId, long SemesterId,
    long OrganizationId, DateTime EndAt);
