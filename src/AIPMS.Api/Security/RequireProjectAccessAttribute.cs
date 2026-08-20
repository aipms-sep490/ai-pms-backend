using AIPMS.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;

namespace AIPMS.Api.Security;

public sealed class RequireProjectAccessAttribute : AuthorizeAttribute
{
    public RequireProjectAccessAttribute()
    {
        Policy = AuthorizationPolicies.ProjectAccess;
    }
}
