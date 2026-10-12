using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Models;
using AIPMS.Application.Features.Projects.Queries;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class GetProjectProgressAnalysisQueryHandlerTests
{
    [Fact]
    public async Task Get_uses_scoped_analysis_without_creating_history()
    {
        var service = new ScopedAnalysisStub();
        var handler = new GetProjectProgressAnalysisQueryHandler(service);
        using var cancellation = new CancellationTokenSource();
        await handler.Handle(new(101), cancellation.Token);
        Assert.Equal(101, service.Project);
        Assert.False(service.Record);
        Assert.Equal(cancellation.Token, service.Token);
    }
    [Fact]
    public async Task Get_propagates_scope_denial_without_fallback_to_project_access()
    {
        var handler = new GetProjectProgressAnalysisQueryHandler(new ScopedAnalysisStub { Denied = true });
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(101), CancellationToken.None));
    }
    private sealed class ScopedAnalysisStub : IScopedProgressAnalysisService
    {
        public long Project; public bool Record; public CancellationToken Token; public bool Denied;
        public Task<ProjectProgressAnalysisDto> AnalyzeAsync(long projectId, bool record, CancellationToken ct)
        {
            Project = projectId; Record = record; Token = ct;
            if (Denied) throw new ForbiddenException();
            return Task.FromResult(new RuleBasedProgressAnalysisService().Analyze(new(projectId, "ACTIVE", 0, 0, [], [], [], []), DateTime.UtcNow, ct));
        }
    }
}

internal sealed class StubProjectAccessService : AIPMS.Application.Abstractions.Security.IProjectAccessService
{
    public bool CanAccess { get; set; } = true;

    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CanAccess);
    }
}
