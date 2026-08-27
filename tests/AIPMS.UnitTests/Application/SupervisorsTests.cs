using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorProfile;
using AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorExpertise;
using AIPMS.Application.Features.Supervisors.Commands.SendSupervisorRequest;
using AIPMS.Application.Features.Supervisors.Commands.RejectSupervisorRequest;
using AIPMS.Application.Features.Supervisors.Commands.AcceptSupervisorRequest;
using AIPMS.Application.Features.Supervisors.Commands.CancelSupervisorRequest;
using AIPMS.Application.Features.Supervisors.Commands.EndSupervisorAssignment;
using AIPMS.Application.Features.Supervisors.Queries.GetSupervisors;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Domain.Entities;
using AIPMS.Domain.Exceptions;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorsTests
{
    [Theory]
    [InlineData(0, 10, false)]
    [InlineData(-1, 10, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, -5, false)]
    [InlineData(1, 101, false)]
    [InlineData(1, 20, true)]
    public async Task GetSupervisorsQueryValidator_ValidatesPagination(int pageNumber, int pageSize, bool isValid)
    {
        var validator = new GetSupervisorsQueryValidator();
        var query = new GetSupervisorsQuery(pageNumber, pageSize, null, null, null);

        var result = await validator.ValidateAsync(query);

        Assert.Equal(isValid, result.IsValid);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(null, true)]
    public async Task UpdateSupervisorProfileCommandValidator_ValidatesMaxActiveProjects(int? maxActiveProjects, bool isValid)
    {
        var validator = new UpdateSupervisorProfileCommandValidator();
        var command = new UpdateSupervisorProfileCommand("Bio Test", maxActiveProjects, true);

        var result = await validator.ValidateAsync(command);

        Assert.Equal(isValid, result.IsValid);
    }

    [Fact]
    public async Task UpdateSupervisorExpertiseCommandValidator_InvalidExpertiseName_ReturnsValidationError()
    {
        var validator = new UpdateSupervisorExpertiseCommandValidator();
        var command = new UpdateSupervisorExpertiseCommand(new List<SupervisorExpertiseDto>
        {
            new("", "Expert"),
            new("AI", "Intermediate")
        });

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("ExpertiseName"));
    }

    [Fact]
    public async Task UpdateSupervisorExpertiseCommandValidator_ValidExpertises_ReturnsNoValidationError()
    {
        var validator = new UpdateSupervisorExpertiseCommandValidator();
        var command = new UpdateSupervisorExpertiseCommand(new List<SupervisorExpertiseDto>
        {
            new("AI", "Expert"),
            new("Web Development", "Intermediate")
        });

        var result = await validator.ValidateAsync(command);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, 10, false)]
    [InlineData(10, 0, false)]
    [InlineData(10, 10, true)]
    public async Task SendSupervisorRequestCommandValidator_ValidatesIds(long projectId, long supervisorId, bool isValid)
    {
        var validator = new SendSupervisorRequestCommandValidator();
        var command = new SendSupervisorRequestCommand(projectId, supervisorId, "Test");

        var result = await validator.ValidateAsync(command);

        Assert.Equal(isValid, result.IsValid);
    }

    [Fact]
    public async Task SendSupervisorRequestCommandHandler_ValidInputs_CreatesPendingRequest()
    {
        var currentUser = new FakeCurrentUser(5);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });
        requestRepo.ExistingProjectIds.Add(100);

        var handler = CreateSendHandler(currentUser, supervisorRepo, requestRepo);
        var command = new SendSupervisorRequestCommand(100, 20, "Hello");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal(100, result.ProjectId);
        Assert.Equal(20, result.SupervisorProfileId);
        Assert.Equal(5, result.RequestedBy);
        Assert.Equal("PENDING", result.Status);
        Assert.Equal("Hello", result.RequestMessage);
    }

    [Fact]
    public async Task SendSupervisorRequestCommandHandler_ProjectNotFound_ThrowsNotFoundException()
    {
        var currentUser = new FakeCurrentUser(5);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });

        var handler = CreateSendHandler(currentUser, supervisorRepo, requestRepo);
        var command = new SendSupervisorRequestCommand(100, 20, "Hello");

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SendSupervisorRequestCommandHandler_SupervisorNotFound_ThrowsNotFoundException()
    {
        var currentUser = new FakeCurrentUser(5);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        requestRepo.ExistingProjectIds.Add(100);

        var handler = CreateSendHandler(currentUser, supervisorRepo, requestRepo);
        var command = new SendSupervisorRequestCommand(100, 20, "Hello");

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SendSupervisorRequestCommandHandler_SupervisorNotAvailable_ThrowsDomainException()
    {
        var currentUser = new FakeCurrentUser(5);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = false });
        requestRepo.ExistingProjectIds.Add(100);

        var handler = CreateSendHandler(currentUser, supervisorRepo, requestRepo);
        var command = new SendSupervisorRequestCommand(100, 20, "Hello");

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SendSupervisorRequestCommandHandler_DuplicatePendingRequest_ThrowsConflictException()
    {
        var currentUser = new FakeCurrentUser(5);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });
        requestRepo.ExistingProjectIds.Add(100);
        requestRepo.Requests.Add(new SupervisorRequest { ProjectId = 100, SupervisorProfileId = 20, Status = "PENDING" });

        var handler = CreateSendHandler(currentUser, supervisorRepo, requestRepo);
        var command = new SendSupervisorRequestCommand(100, 20, "Hello");

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task RejectSupervisorRequestCommandHandler_ValidInputs_RejectsRequest()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });
        var req = new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 20, Status = "PENDING" };
        requestRepo.Requests.Add(req);

        var handler = new RejectSupervisorRequestCommandHandler(currentUser, supervisorRepo, requestRepo);
        var command = new RejectSupervisorRequestCommand(1, "No capacity");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(Unit.Value, result);
        Assert.Equal("REJECTED", req.Status);
        Assert.Equal("No capacity", req.ResponseMessage);
        Assert.NotNull(req.RespondedAt);
    }

    [Fact]
    public async Task RejectSupervisorRequestCommandHandler_RequestNotFound_ThrowsNotFoundException()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });

        var handler = new RejectSupervisorRequestCommandHandler(currentUser, supervisorRepo, requestRepo);
        var command = new RejectSupervisorRequestCommand(1, "No capacity");

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task RejectSupervisorRequestCommandHandler_RequestNotPending_ThrowsConflictException()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });
        var req = new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 20, Status = "ACCEPTED" };
        requestRepo.Requests.Add(req);

        var handler = new RejectSupervisorRequestCommandHandler(currentUser, supervisorRepo, requestRepo);
        var command = new RejectSupervisorRequestCommand(1, "No capacity");

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task RejectSupervisorRequestCommandHandler_WrongSupervisor_ThrowsForbiddenException()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });
        var req = new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 30, Status = "PENDING" };
        requestRepo.Requests.Add(req);

        var handler = new RejectSupervisorRequestCommandHandler(currentUser, supervisorRepo, requestRepo);
        var command = new RejectSupervisorRequestCommand(1, "No capacity");

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task AcceptSupervisorRequestCommandHandler_ValidInputs_CreatesAssignment()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();
        var assignmentRepo = new FakeSupervisorAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true, MaxActiveProjects = 3 });
        var req = new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 20, Status = "PENDING" };
        requestRepo.Requests.Add(req);

        var handler = new AcceptSupervisorRequestCommandHandler(currentUser, supervisorRepo, requestRepo, assignmentRepo, unitOfWork);
        var command = new AcceptSupervisorRequestCommand(1);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(Unit.Value, result);
        Assert.Equal("ACCEPTED", req.Status);
        Assert.NotNull(req.RespondedAt);
        Assert.Single(assignmentRepo.Assignments);
        var assignment = assignmentRepo.Assignments[0];
        Assert.Equal(100, assignment.ProjectId);
        Assert.Equal(20, assignment.SupervisorProfileId);
        Assert.Equal(1, assignment.SupervisorRequestId);
        Assert.True(assignment.IsPrimary);
        Assert.Null(assignment.EndedAt);
        Assert.True(unitOfWork.TransactionStarted);
        Assert.True(unitOfWork.Committed);
        Assert.Equal([100L], requestRepo.InitializedWorkspaceProjects);
    }

    [Fact]
    public async Task AcceptSupervisorRequestCommandHandler_CapacityExceeded_ThrowsConflictException()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();
        var assignmentRepo = new FakeSupervisorAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true, MaxActiveProjects = 2 });
        var req = new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 20, Status = "PENDING" };
        requestRepo.Requests.Add(req);
        assignmentRepo.Assignments.Add(new SupervisorAssignment { ProjectId = 101, SupervisorProfileId = 20, EndedAt = null });
        assignmentRepo.Assignments.Add(new SupervisorAssignment { ProjectId = 102, SupervisorProfileId = 20, EndedAt = null });

        var handler = new AcceptSupervisorRequestCommandHandler(currentUser, supervisorRepo, requestRepo, assignmentRepo, unitOfWork);
        var command = new AcceptSupervisorRequestCommand(1);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
        Assert.Equal("PENDING", req.Status);
        Assert.True(unitOfWork.TransactionStarted);
        Assert.True(unitOfWork.RolledBack);
    }

    [Fact]
    public async Task AcceptSupervisorRequestCommandHandler_WrongSupervisor_ThrowsForbiddenException()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();
        var assignmentRepo = new FakeSupervisorAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork();

        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });
        var req = new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 30, Status = "PENDING" };
        requestRepo.Requests.Add(req);

        var handler = new AcceptSupervisorRequestCommandHandler(currentUser, supervisorRepo, requestRepo, assignmentRepo, unitOfWork);
        var command = new AcceptSupervisorRequestCommand(1);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task AcceptSupervisorRequestCommandHandler_AlreadyAccepted_IsIdempotent()
    {
        var currentUser = new FakeCurrentUser(10);
        var supervisorRepo = new FakeSupervisorRepository();
        var requestRepo = new FakeSupervisorRequestRepository();
        var assignmentRepo = new FakeSupervisorAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork();
        supervisorRepo.Profiles.Add(new SupervisorProfile { Id = 20, UserId = 10, IsAvailable = true });
        requestRepo.Requests.Add(new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 20, Status = "ACCEPTED" });
        assignmentRepo.Assignments.Add(new SupervisorAssignment
        {
            Id = 1, ProjectId = 100, SupervisorProfileId = 20, SupervisorRequestId = 1, AssignedAt = DateTime.UtcNow
        });
        var handler = new AcceptSupervisorRequestCommandHandler(
            currentUser, supervisorRepo, requestRepo, assignmentRepo, unitOfWork);

        await handler.Handle(new AcceptSupervisorRequestCommand(1), CancellationToken.None);
        await handler.Handle(new AcceptSupervisorRequestCommand(1), CancellationToken.None);

        Assert.Single(assignmentRepo.Assignments);
        Assert.Equal([100L], requestRepo.InitializedWorkspaceProjects);
        Assert.True(unitOfWork.Committed);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(10, true)]
    public async Task EndSupervisorAssignmentCommandValidator_ValidatesId(long id, bool isValid)
    {
        var validator = new EndSupervisorAssignmentCommandValidator();
        var command = new EndSupervisorAssignmentCommand(id);

        var result = await validator.ValidateAsync(command);

        Assert.Equal(isValid, result.IsValid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task RemainingSupervisorIdValidators_ValidatePositiveIds(long id, bool isValid)
    {
        var results = await Task.WhenAll(
            new AIPMS.Application.Features.Supervisors.Commands.AcceptSupervisorRequest.AcceptSupervisorRequestCommandValidator()
                .ValidateAsync(new AcceptSupervisorRequestCommand(id)),
            new AIPMS.Application.Features.Supervisors.Commands.CancelSupervisorRequest.CancelSupervisorRequestCommandValidator()
                .ValidateAsync(new CancelSupervisorRequestCommand(id)),
            new AIPMS.Application.Features.Supervisors.Commands.RejectSupervisorRequest.RejectSupervisorRequestCommandValidator()
                .ValidateAsync(new RejectSupervisorRequestCommand(id, null)),
            new AIPMS.Application.Features.Supervisors.Queries.GetSupervisorById.GetSupervisorByIdQueryValidator()
                .ValidateAsync(new AIPMS.Application.Features.Supervisors.Queries.GetSupervisorById.GetSupervisorByIdQuery(id)),
            new AIPMS.Application.Features.Supervisors.Queries.GetProjectSupervisor.GetProjectSupervisorQueryValidator()
                .ValidateAsync(new AIPMS.Application.Features.Supervisors.Queries.GetProjectSupervisor.GetProjectSupervisorQuery(id)),
            new AIPMS.Application.Features.Supervisors.Queries.GetSupervisorCandidates.GetSupervisorCandidatesQueryValidator()
                .ValidateAsync(new AIPMS.Application.Features.Supervisors.Queries.GetSupervisorCandidates.GetSupervisorCandidatesQuery(id, null)));

        Assert.All(results, result => Assert.Equal(isValid, result.IsValid));
    }

    [Fact]
    public async Task EndSupervisorAssignmentCommandHandler_ValidInput_EndsAssignment()
    {
        var currentUser = new FakeCurrentUser(10);
        var assignmentRepo = new FakeSupervisorAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork();

        var assignment = new SupervisorAssignment { Id = 1, ProjectId = 100, SupervisorProfileId = 20, EndedAt = null };
        assignmentRepo.Assignments.Add(assignment);

        var handler = new EndSupervisorAssignmentCommandHandler(currentUser, new FakeProjectAccessService(), assignmentRepo, unitOfWork);
        var command = new EndSupervisorAssignmentCommand(1);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(Unit.Value, result);
        Assert.NotNull(assignment.EndedAt);
        Assert.True(unitOfWork.SavedChanges);
    }

    [Fact]
    public async Task EndSupervisorAssignmentCommandHandler_AssignmentNotFound_ThrowsNotFoundException()
    {
        var currentUser = new FakeCurrentUser(10);
        var assignmentRepo = new FakeSupervisorAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork();

        var handler = new EndSupervisorAssignmentCommandHandler(currentUser, new FakeProjectAccessService(), assignmentRepo, unitOfWork);
        var command = new EndSupervisorAssignmentCommand(1);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task EndSupervisorAssignmentCommandHandler_AlreadyEnded_ThrowsConflictException()
    {
        var currentUser = new FakeCurrentUser(10);
        var assignmentRepo = new FakeSupervisorAssignmentRepository();
        var unitOfWork = new FakeUnitOfWork();

        var assignment = new SupervisorAssignment { Id = 1, ProjectId = 100, SupervisorProfileId = 20, EndedAt = DateTime.UtcNow.AddDays(-1) };
        assignmentRepo.Assignments.Add(assignment);

        var handler = new EndSupervisorAssignmentCommandHandler(currentUser, new FakeProjectAccessService(), assignmentRepo, unitOfWork);
        var command = new EndSupervisorAssignmentCommand(1);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    private sealed class FakeCurrentUser(long? id) : ICurrentUser
    {
        public bool IsAuthenticated => id.HasValue;
        public long? UserId => id;
        public string? Email => "test@aipms.com";
        public string? FullName => "Test User";
        public IReadOnlyCollection<string> Roles => ["LECTURER"];
    }

    [Fact]
    public async Task CancelSupervisorRequestCommandHandler_PendingRequest_CancelsRequest()
    {
        var requestRepository = new FakeSupervisorRequestRepository();
        var supervisorRequest = new SupervisorRequest { Id = 1, ProjectId = 100, SupervisorProfileId = 20, RequestedBy = 5, Status = "PENDING" };
        requestRepository.Requests.Add(supervisorRequest);
        var handler = new CancelSupervisorRequestCommandHandler(new FakeCurrentUser(5), requestRepository);

        await handler.Handle(new CancelSupervisorRequestCommand(1), CancellationToken.None);

        Assert.Equal("CANCELLED", supervisorRequest.Status);
        Assert.NotNull(supervisorRequest.RespondedAt);
    }

    [Fact]
    public async Task CancelSupervisorRequestCommandHandler_NonPendingRequest_ThrowsConflictException()
    {
        var requestRepository = new FakeSupervisorRequestRepository();
        requestRepository.Requests.Add(new SupervisorRequest { Id = 1, ProjectId = 100, RequestedBy = 5, Status = "ACCEPTED" });
        var handler = new CancelSupervisorRequestCommandHandler(new FakeCurrentUser(5), requestRepository);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new CancelSupervisorRequestCommand(1), CancellationToken.None));
    }

    [Fact]
    public async Task CancelSupervisorRequestCommandHandler_DifferentActor_ThrowsForbiddenException()
    {
        var requestRepository = new FakeSupervisorRequestRepository();
        requestRepository.Requests.Add(new SupervisorRequest { Id = 1, ProjectId = 100, RequestedBy = 7, Status = "PENDING" });
        var handler = new CancelSupervisorRequestCommandHandler(new FakeCurrentUser(5), requestRepository);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new CancelSupervisorRequestCommand(1), CancellationToken.None));
    }

    private static SendSupervisorRequestCommandHandler CreateSendHandler(
        ICurrentUser currentUser,
        ISupervisorRepository supervisorRepository,
        ISupervisorRequestRepository requestRepository) =>
        new(currentUser, new FakeProjectAccessService(), supervisorRepository, requestRepository,
            new FakeSupervisorAssignmentRepository());

    private sealed class FakeProjectAccessService : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeSupervisorRepository : ISupervisorRepository
    {
        public List<SupervisorProfile> Profiles { get; } = new();

        public Task<IReadOnlyList<SupervisorCandidateDto>> GetEligibleCandidatesAsync(string? expertise, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SupervisorCandidateDto>>([]);

        public Task<PagedResult<SupervisorDto>> GetPagedSupervisorsAsync(
            int pageNumber, int pageSize, string? search, bool? isAvailable, string? expertise, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SupervisorDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<SupervisorProfile?> GetProfileByIdAsync(long id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Profiles.FirstOrDefault(p => p.Id == id));
        }

        public Task<SupervisorProfile?> GetProfileByUserIdAsync(long userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Profiles.FirstOrDefault(p => p.UserId == userId));
        }

        public Task<SupervisorProfile?> GetProfileByUserIdForUpdateAsync(long userId, CancellationToken cancellationToken) =>
            GetProfileByUserIdAsync(userId, cancellationToken);

        public Task UpdateProfileAsync(SupervisorProfile profile, CancellationToken cancellationToken)
        {
            var existing = Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (existing != null)
            {
                existing.Bio = profile.Bio;
                existing.MaxActiveProjects = profile.MaxActiveProjects;
                existing.IsAvailable = profile.IsAvailable;
            }
            return Task.CompletedTask;
        }

        public Task UpdateExpertisesAsync(long supervisorProfileId, IEnumerable<SupervisorExpertise> expertises, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ExistsAsync(long id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Profiles.Any(p => p.Id == id));
        }
    }

    private sealed class FakeSupervisorRequestRepository : ISupervisorRequestRepository
    {
        public List<SupervisorRequest> Requests { get; } = new();
        public List<long> ExistingProjectIds { get; } = new();
        public List<long> InitializedWorkspaceProjects { get; } = new();

        public Task AddAsync(SupervisorRequest request, CancellationToken cancellationToken)
        {
            request.Id = Requests.Count + 1;
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<SupervisorRequest?> GetByIdAsync(long id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Requests.FirstOrDefault(r => r.Id == id));
        }

        public Task<SupervisorRequest?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken) =>
            GetByIdAsync(id, cancellationToken);

        public Task<bool> HasPendingRequestAsync(long projectId, long supervisorProfileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Requests.Any(r => r.ProjectId == projectId
                                                  && r.SupervisorProfileId == supervisorProfileId
                                                  && r.Status == "PENDING"));
        }

        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken)
        {
            return Task.FromResult(ExistingProjectIds.Contains(projectId));
        }

        public Task<bool> IsProjectApprovedAsync(long projectId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task ActivateProjectAsync(long projectId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InitializeProjectWorkspaceAsync(long projectId, long actorUserId, CancellationToken cancellationToken)
        {
            if (!InitializedWorkspaceProjects.Contains(projectId)) InitializedWorkspaceProjects.Add(projectId);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SupervisorRequest request, CancellationToken cancellationToken)
        {
            var existing = Requests.FirstOrDefault(r => r.Id == request.Id);
            if (existing != null)
            {
                existing.Status = request.Status;
                existing.ResponseMessage = request.ResponseMessage;
                existing.RespondedAt = request.RespondedAt;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSupervisorAssignmentRepository : ISupervisorAssignmentRepository
    {
        public List<SupervisorAssignment> Assignments { get; } = new();

        public Task AddAsync(SupervisorAssignment assignment, CancellationToken cancellationToken)
        {
            assignment.Id = Assignments.Count + 1;
            Assignments.Add(assignment);
            return Task.CompletedTask;
        }

        public Task<int> CountActiveAssignmentsAsync(long supervisorProfileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Assignments.Count(a => a.SupervisorProfileId == supervisorProfileId && a.EndedAt == null));
        }

        public Task<SupervisorAssignment?> GetActiveAssignmentByProjectAsync(long projectId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Assignments.FirstOrDefault(a => a.ProjectId == projectId && a.EndedAt == null));
        }

        public Task<SupervisorAssignment?> GetByIdAsync(long id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Assignments.FirstOrDefault(a => a.Id == id));
        }

        public Task<SupervisorAssignment?> GetByRequestIdAsync(long requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Assignments.FirstOrDefault(a => a.SupervisorRequestId == requestId));

        public Task UpdateAsync(SupervisorAssignment assignment, CancellationToken cancellationToken)
        {
            var existing = Assignments.FirstOrDefault(a => a.Id == assignment.Id);
            if (existing != null)
            {
                existing.EndedAt = assignment.EndedAt;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public bool TransactionStarted { get; private set; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public bool SavedChanges { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SavedChanges = true;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken)
        {
            TransactionStarted = true;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            RolledBack = true;
            return Task.CompletedTask;
        }
    }
}
