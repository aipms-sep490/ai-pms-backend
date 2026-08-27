using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.Commands;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Queries;

namespace AIPMS.UnitTests.Application;

public sealed class DeliverableWorkflowTests
{
    [Fact]
    public async Task Submit_UsesSequentialRepositoryResult_AndPersistsImmutableMetadata()
    {
        var repo=new FakeRepository();var storage=new FakeStorage();
        var handler=new SubmitDeliverableVersionCommandHandler(new User(7),repo,storage,TimeProvider.System);
        var result=await handler.Handle(Command("application/pdf"),default);
        Assert.Equal(1,result.VersionNumber);Assert.Equal("SUBMITTED",result.Status);
        Assert.Equal(64,result.Files.Single().ChecksumSha256!.Length);Assert.True(storage.Exists);
    }

    [Fact]
    public async Task Submit_RejectsOutsiderBeforeWritingStorage()
    {
        var repo=new FakeRepository{IsMember=false};var storage=new FakeStorage();
        var handler=new SubmitDeliverableVersionCommandHandler(new User(7),repo,storage,TimeProvider.System);
        await Assert.ThrowsAsync<ForbiddenException>(()=>handler.Handle(Command("application/pdf"),default));
        Assert.False(storage.Exists);
    }

    [Fact]
    public async Task Submit_RejectsMimeAndSize()
    {
        var handler=new SubmitDeliverableVersionCommandHandler(new User(7),new FakeRepository(),new FakeStorage(),TimeProvider.System);
        await Assert.ThrowsAsync<ValidationException>(()=>handler.Handle(Command("application/x-msdownload"),default));
        await Assert.ThrowsAsync<ValidationException>(()=>handler.Handle(new SubmitDeliverableVersionCommand(1,null,"x.pdf","application/pdf",26*1024*1024,new MemoryStream([1])),default));
    }

    [Fact]
    public async Task DatabaseFailure_CleansUpStoredObject()
    {
        var repo=new FakeRepository{FailWrite=true};var storage=new FakeStorage();
        var handler=new SubmitDeliverableVersionCommandHandler(new User(7),repo,storage,TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>handler.Handle(Command("application/pdf"),default));
        Assert.False(storage.Exists);Assert.True(storage.DeleteCalled);
    }

    [Fact]
    public async Task StorageFailure_DoesNotWriteMetadata()
    {
        var repo=new FakeRepository();var storage=new FakeStorage{FailStore=true};
        var handler=new SubmitDeliverableVersionCommandHandler(new User(7),repo,storage,TimeProvider.System);
        await Assert.ThrowsAsync<IOException>(()=>handler.Handle(Command("application/pdf"),default));
        Assert.False(repo.WriteCalled);
    }

    [Fact]
    public async Task SubmittedFile_IsImmutable()
    {
        var repo=new FakeRepository{FileImmutable=true};var storage=new FakeStorage{Exists=true};
        var handler=new DeleteDeliverableFileCommandHandler(new User(7),new Access(),repo,storage);
        await Assert.ThrowsAsync<ConflictException>(()=>handler.Handle(new DeleteDeliverableFileCommand(9),default));
        Assert.False(storage.DeleteCalled);
    }

    [Fact]
    public async Task Feedback_RequiresCorrectActiveAssignment()
    {
        var repo=new FakeRepository{AssignedSupervisor=false};
        var handler=new AddSupervisorFeedbackCommandHandler(new User(7),repo);
        await Assert.ThrowsAsync<ForbiddenException>(()=>handler.Handle(new AddSupervisorFeedbackCommand(1,"review"),default));
        Assert.False(repo.FeedbackAdded);
    }

    [Fact]
    public async Task Feedback_FromAssignedSupervisor_IsStored()
    {
        var repo=new FakeRepository();var handler=new AddSupervisorFeedbackCommandHandler(new User(7),repo);
        await handler.Handle(new AddSupervisorFeedbackCommand(1,"review"),default);Assert.True(repo.FeedbackAdded);
    }

    [Fact]
    public async Task Download_UsesAuthorizedApplicationServiceAndStorage()
    {
        var storage=new FakeStorage{Exists=true};var handler=new DownloadDeliverableFileQueryHandler(new User(7),new Access(),new FakeRepository(),storage);
        var result=await handler.Handle(new DownloadDeliverableFileQuery(9),default);Assert.Equal("x.pdf",result.OriginalFileName);Assert.Equal(3,result.Content.Length);
    }

    [Fact]
    public async Task Submit_RejectsExpiredDeadline()
    {
        var repo=new FakeRepository{DueAt=DateTime.UtcNow.AddMinutes(-1)};var handler=new SubmitDeliverableVersionCommandHandler(new User(7),repo,new FakeStorage(),TimeProvider.System);
        await Assert.ThrowsAsync<ConflictException>(()=>handler.Handle(Command("application/pdf"),default));Assert.False(repo.WriteCalled);
    }

    private static SubmitDeliverableVersionCommand Command(string mime)=>new(1,"version note","result.pdf",mime,3,new MemoryStream([1,2,3]));

    private sealed record User(long Id):ICurrentUser
    { public bool IsAuthenticated=>true;public long? UserId=>Id;public string? Email=>null;public string? FullName=>null;public IReadOnlyCollection<string> Roles=>[]; }
    private sealed class Access:IProjectAccessService{public Task<bool> CanAccessAsync(long userId,long projectId,CancellationToken cancellationToken=default)=>Task.FromResult(true);}
    private sealed class FakeStorage:IFileStorage
    { public bool Exists;public bool DeleteCalled;public bool FailStore;public Task StoreAsync(string key,Stream content,CancellationToken ct){if(FailStore)throw new IOException("storage");Exists=true;return Task.CompletedTask;}public Task<Stream> OpenReadAsync(string key,CancellationToken ct)=>Task.FromResult<Stream>(new MemoryStream([1,2,3]));public Task DeleteAsync(string key,CancellationToken ct){DeleteCalled=true;Exists=false;return Task.CompletedTask;} }
    private sealed class FakeRepository:IDeliverableRepository
    {
        public bool IsMember=true,FailWrite,WriteCalled,FileImmutable,AssignedSupervisor=true,FeedbackAdded;public DateTime? DueAt=DateTime.UtcNow.AddHours(1);
        private readonly List<DeliverableVersionDto> history=[];
        public Task<bool> ProjectExistsAsync(long id,CancellationToken ct)=>Task.FromResult(true);
        public Task<bool> IsProjectActiveAsync(long id,CancellationToken ct)=>Task.FromResult(true);
        public Task<bool> IsActiveTeamMemberAsync(long user,long project,CancellationToken ct)=>Task.FromResult(IsMember);
        public Task<DeliverableDto?> GetByIdAsync(long id,CancellationToken ct)=>Task.FromResult<DeliverableDto?>(new(id,10,null,"D",null,null,null,"DRAFT",7,DateTime.UtcNow));
        public Task<PagedResult<DeliverableDto>> GetPagedAsync(long p,int page,int size,CancellationToken ct)=>Task.FromResult(new PagedResult<DeliverableDto>([],page,size,0));
        public Task<long> CreateAsync(long p,long? m,string t,string? d,string? type,DateTime? due,long u,CancellationToken ct)=>Task.FromResult(1L);
        public Task UpdateAsync(long id,string t,string? d,string? type,DateTime? due,CancellationToken ct)=>Task.CompletedTask;
        public Task DeleteAsync(long id,CancellationToken ct)=>Task.CompletedTask;
        public Task<SubmissionTarget?> GetSubmissionTargetAsync(long id,CancellationToken ct)=>Task.FromResult<SubmissionTarget?>(new(id,10,DueAt,"DRAFT"));
        public Task<long?> GetActiveSupervisorAssignmentIdAsync(long id,CancellationToken ct)=>Task.FromResult<long?>(5);
        public Task<long> AddVersionAndFileAsync(VersionSubmission s,CancellationToken ct){WriteCalled=true;if(FailWrite)throw new InvalidOperationException("db");var dto=new DeliverableVersionDto(1,s.DeliverableId,history.Count+1,s.SubmittedBy,s.Note,"SUBMITTED",DateTime.UtcNow,[new FileMetadataDto(9,s.OriginalFileName,s.MimeType,s.Size,s.Checksum,DateTime.UtcNow,s.SubmittedBy)],[]);history.Add(dto);return Task.FromResult(1L);}
        public Task<IReadOnlyList<DeliverableVersionDto>> GetHistoryAsync(long id,CancellationToken ct)=>Task.FromResult<IReadOnlyList<DeliverableVersionDto>>(history);
        public Task<FileDownloadTarget?> GetFileForDownloadAsync(long id,CancellationToken ct)=>Task.FromResult<FileDownloadTarget?>(new(id,10,"key","x.pdf","application/pdf",FileImmutable));
        public Task<(long ProjectId,long DeliverableId)?> GetVersionParentAsync(long id,CancellationToken ct)=>Task.FromResult<(long,long)?>((10,1));
        public Task DeleteUnsubmittedFileAsync(long id,CancellationToken ct)=>Task.CompletedTask;
        public Task<bool> IsCurrentUserAssignedSupervisorAsync(long u,long p,CancellationToken ct)=>Task.FromResult(AssignedSupervisor);
        public Task AddFeedbackAsync(long v,long a,long p,string text,CancellationToken ct){FeedbackAdded=true;return Task.CompletedTask;}
        public Task RequestRevisionAsync(long d,long v,long a,long p,string reason,CancellationToken ct)=>Task.CompletedTask;
    }
}
