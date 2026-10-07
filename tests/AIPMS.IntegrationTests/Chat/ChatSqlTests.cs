using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Chat.Abstractions;
using AIPMS.Infrastructure.Chat;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Chat;

public sealed class ChatSqlTests(FinalSubmissionDraftDatabaseFixture fixture) : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private readonly Clock clock = new();
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026,10,7,10,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow()=>Now; }
    private ChatService Service(AipmsDbContext db)=>new(db,new ChatAccessService(db,clock),Options.Create(new ChatSettings{Enabled=true}),clock,new ChatWakeSignal());
    private async Task<T> Use<T>(Func<ChatService,Task<T>> action) { await using var db=fixture.CreateContext();return await action(Service(db)); }
    private async Task<ChatConversationDto> Group(FinalDraftScenario s)=>await Use(chat=>chat.OpenAsync(s.Users.Student,"PROJECT",s.ProjectId,default));
    private async Task<ChatMessageDto> Send(long actor,string id,string body="Hello",Guid? key=null,string? reply=null)=>
        await Use(chat=>chat.SendAsync(actor,long.Parse(id),new(key??Guid.NewGuid(),body,reply),default));

    [Fact]
    public async Task Direct_and_official_groups_are_unique_and_admin_has_no_implicit_access()
    {
        var s=await fixture.Seed();
        var groups=await Task.WhenAll(Group(s),Group(s));
        Assert.Equal(groups[0].Id,groups[1].Id);
        var direct=await Use(c=>c.OpenAsync(s.Users.Student,"DIRECT",s.MemberId,default));
        var reverse=await Use(c=>c.OpenAsync(s.MemberId,"DIRECT",s.Users.Student,default));
        Assert.Equal(direct.Id,reverse.Id);
        foreach(var denied in new[]{s.Users.Admin,s.Users.Staff,s.Users.Lecturer,s.Users.OutsideStaff})
        {
            await Assert.ThrowsAsync<NotFoundException>(()=>Use(c=>c.DetailAsync(denied,long.Parse(direct.Id),default)));
            await Assert.ThrowsAsync<NotFoundException>(()=>Use(c=>c.OpenAsync(s.Users.Student,"DIRECT",denied,default)));
        }
    }

    [Fact]
    public async Task Send_is_idempotent_with_ordered_history_unread_and_monotonic_read()
    {
        var s=await fixture.Seed();var group=await Group(s);var key=Guid.NewGuid();
        var first=await Send(s.Users.Student,group.Id,"First",key);
        var retry=await Send(s.Users.Student,group.Id,"First",key);
        Assert.Equal(first.Id,retry.Id);
        await Assert.ThrowsAsync<ConflictException>(()=>Send(s.Users.Student,group.Id,"Different",key));
        var second=await Send(s.Users.Student,group.Id,"Second",reply:first.Id);
        Assert.Equal(first.Id,second.Reply!.Id);
        var inbox=await Use(c=>c.InboxAsync(s.MemberId,null,20,default));
        Assert.Equal(2,inbox.Items.Single().UnreadCount);
        var page=await Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),null,null,1,default));
        Assert.True(page.HasMore);Assert.Equal(second.Id,page.Items.Single().Id);
        var older=await Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),page.NextCursor,null,1,default));
        Assert.Equal(first.Id,older.Items.Single().Id);
        await using(var db=fixture.CreateContext()) await Service(db).ReadAsync(s.MemberId,long.Parse(group.Id),long.Parse(second.Id),default);
        await using(var db=fixture.CreateContext()) await Service(db).ReadAsync(s.MemberId,long.Parse(group.Id),long.Parse(first.Id),default);
        Assert.Equal(0,(await Use(c=>c.DetailAsync(s.MemberId,long.Parse(group.Id),default))).UnreadCount);
        await using var verify=fixture.CreateContext();
        Assert.Equal(2,await verify.Set<M.ChatMessage>().CountAsync(m=>m.ConversationId==long.Parse(group.Id)));
        Assert.Equal(2,await verify.AuditLogs.CountAsync(a=>a.Action=="CHAT_SENT" && a.EntityId==group.Id));
    }

    [Fact]
    public async Task Removal_and_rejoin_without_intervening_reads_cannot_expose_absence_history()
    {
        var s=await fixture.Seed();var group=await Group(s);
        await Send(s.Users.Student,group.Id,"Before removal");
        await using(var db=fixture.CreateContext()) { var member=await db.TeamMembers.SingleAsync(m=>m.TeamId==s.TeamId&&m.UserId==s.MemberId);member.LeftAt=clock.Now.UtcDateTime;await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<NotFoundException>(()=>Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),null,null,50,default)));
        await Send(s.Users.Student,group.Id,"During absence");
        await using(var db=fixture.CreateContext()) { var member=await db.TeamMembers.SingleAsync(m=>m.TeamId==s.TeamId&&m.UserId==s.MemberId);member.LeftAt=null;member.JoinedAt=clock.Now.UtcDateTime;await db.SaveChangesAsync(); }
        var returned=await Send(s.Users.Student,group.Id,"After readmission");
        var history=await Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),null,null,50,default));
        Assert.Equal(returned.Id,Assert.Single(history.Items).Id);
    }

    [Fact]
    public async Task Edit_recall_stale_tokens_and_cross_conversation_replies_are_guarded()
    {
        var s=await fixture.Seed();var group=await Group(s);var sent=await Send(s.Users.Student,group.Id);
        await Assert.ThrowsAsync<ForbiddenException>(()=>Use(c=>c.EditAsync(s.MemberId,long.Parse(group.Id),long.Parse(sent.Id),new("stolen",sent.ConcurrencyToken),default)));
        var edit=await Use(c=>c.EditAsync(s.Users.Student,long.Parse(group.Id),long.Parse(sent.Id),new("Edited",sent.ConcurrencyToken),default));
        await Assert.ThrowsAsync<ConflictException>(()=>Use(c=>c.RecallAsync(s.Users.Student,long.Parse(group.Id),long.Parse(sent.Id),new(sent.ConcurrencyToken),default)));
        var reply=await Send(s.MemberId,group.Id,"Reply",reply:sent.Id);
        var recalled=await Use(c=>c.RecallAsync(s.Users.Student,long.Parse(group.Id),long.Parse(sent.Id),new(edit.ConcurrencyToken),default));
        Assert.Null(recalled.Body);
        var history=await Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),null,null,50,default));
        Assert.True(history.Items.Single(m=>m.Id==reply.Id).Reply!.Unavailable);
        var team=await Use(c=>c.OpenAsync(s.Users.Student,"TEAM",s.TeamId,default));
        await Assert.ThrowsAsync<NotFoundException>(()=>Send(s.Users.Student,team.Id,reply:reply.Id));
        await Assert.ThrowsAsync<ArgumentException>(()=>Use(c=>c.MessagesAsync(s.Users.Student,long.Parse(team.Id),group.Id+":1",null,50,default)));
        var latest=await Send(s.Users.Student,group.Id);
        clock.Now=clock.Now.AddMinutes(16);
        await Assert.ThrowsAsync<ConflictException>(()=>Use(c=>c.EditAsync(s.Users.Student,long.Parse(group.Id),long.Parse(latest.Id),new("Too late",latest.ConcurrencyToken),default)));
    }

    [Fact]
    public async Task Closed_project_is_read_only_for_captured_members_not_new_members()
    {
        var s=await fixture.Seed();var group=await Group(s);await Send(s.Users.Student,group.Id);
        await using(var db=fixture.CreateContext()) { (await db.Projects.FindAsync(s.ProjectId))!.Status="COMPLETED";await db.SaveChangesAsync(); }
        var detail=await Use(c=>c.DetailAsync(s.MemberId,long.Parse(group.Id),default));
        Assert.False(detail.CanSend);
        await Assert.ThrowsAsync<ConflictException>(()=>Send(s.Users.Student,group.Id));
        Assert.Single((await Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),null,null,50,default))).Items);
        await using(var db=fixture.CreateContext()) { (await db.Users.FindAsync(s.MemberId))!.Status="INACTIVE";await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<ForbiddenException>(()=>Use(c=>c.DetailAsync(s.MemberId,long.Parse(group.Id),default)));
    }

    [Fact]
    public async Task Concurrent_sends_allocate_unique_sequence_and_atomic_outbox()
    {
        var s=await fixture.Seed();var group=await Group(s);
        var messages=await Task.WhenAll(Send(s.Users.Student,group.Id,"a"),Send(s.MemberId,group.Id,"b"));
        Assert.Equal(2,messages.Select(m=>m.Sequence).Distinct().Count());
        await using var db=fixture.CreateContext();
        Assert.Equal(2,await db.Set<M.ChatOutbox>().CountAsync(o=>o.ConversationId==long.Parse(group.Id)&&o.EventType=="MessagesChanged"));
    }

    [Fact]
    public async Task Audit_failure_rolls_back_message_sequence_and_outbox()
    {
        var s=await fixture.Seed();var group=await Group(s);
        var options=new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(fixture.ConnectionString).AddInterceptors(new RejectAudit()).Options;
        await using(var db=new AipmsDbContext(options))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Service(db).SendAsync(s.Users.Student,long.Parse(group.Id),new(Guid.NewGuid(),"Rollback"),default));
        var sent=await Send(s.Users.Student,group.Id,"Committed");Assert.Equal("1",sent.Sequence);
    }
    private sealed class RejectAudit:SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {
            if(eventData.Context!.ChangeTracker.Entries<M.AuditLog>().Any(e=>e.State==EntityState.Added&&e.Entity.Action=="CHAT_SENT")) throw new InvalidOperationException("Injected audit failure");
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Migration_rerun_keeps_existing_chat_rows()
    {
        var s=await fixture.Seed();var group=await Group(s);var sent=await Send(s.Users.Student,group.Id);
        await fixture.Migrate();await fixture.Migrate();
        Assert.Equal(sent.Id,(await Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),null,null,20,default))).Items.Single().Id);
    }

    [Theory]
    [InlineData("PRIMARY")]
    [InlineData("DISCIPLINE_MENTOR")]
    public async Task Assigned_lecturer_can_chat_only_while_assignment_is_active(string type)
    {
        var s=await fixture.Seed();var group=await Group(s);
        await Send(s.Users.Student,group.Id,"Before assignment");
        long assignment;
        await using(var db=fixture.CreateContext())
        {
            var major=await db.ProjectMajors.Where(m=>m.ProjectId==s.ProjectId).Select(m=>m.MajorId).SingleAsync();
            var a=new M.SupervisorAssignment {ProjectId=s.ProjectId,SupervisorProfileId=s.Users.ProfileId,AssignmentType=type,IsPrimary=type=="PRIMARY",
                MajorId=type=="PRIMARY"?null:major,AssignedAt=clock.Now.UtcDateTime,
                SupervisorRequest=new(){ProjectId=s.ProjectId,SupervisorProfileId=s.Users.ProfileId,RequestedBy=s.Users.Student,Status="ACCEPTED",AssignmentType=type,MajorId=type=="PRIMARY"?null:major}};
            db.SupervisorAssignments.Add(a);await db.SaveChangesAsync();assignment=a.Id;
        }
        var first=await Send(s.Users.Lecturer,group.Id,"Assigned");
        Assert.Equal(first.Id,(await Use(c=>c.MessagesAsync(s.Users.Lecturer,long.Parse(group.Id),null,null,50,default))).Items.Single().Id);
        var direct=await Use(c=>c.OpenAsync(s.Users.Lecturer,"DIRECT",s.Users.Student,default));
        await using(var db=fixture.CreateContext()) { (await db.SupervisorAssignments.FindAsync(assignment))!.EndedAt=clock.Now.UtcDateTime;await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<NotFoundException>(()=>Use(c=>c.DetailAsync(s.Users.Lecturer,long.Parse(group.Id),default)));
        await Assert.ThrowsAsync<NotFoundException>(()=>Use(c=>c.DetailAsync(s.Users.Student,long.Parse(direct.Id),default)));
    }

    [Theory]
    [InlineData("ROLE")]
    [InlineData("DEPARTMENT")]
    public async Task Revocation_and_restore_do_not_reopen_old_history(string source)
    {
        var s=await fixture.Seed();var group=await Group(s);await Send(s.Users.Student,group.Id,"Private old message");
        await using(var db=fixture.CreateContext())
        {
            if(source=="ROLE")
            {
                var role=await db.UserRoles.SingleAsync(r=>r.UserId==s.MemberId);var roleId=role.RoleId;
                db.UserRoles.Remove(role);await db.SaveChangesAsync();
                db.UserRoles.Add(new(){UserId=s.MemberId,RoleId=roleId});await db.SaveChangesAsync();
            }
            else
            {
                var department=(await db.Departments.FindAsync(s.Users.DepartmentId))!;
                department.IsActive=false;await db.SaveChangesAsync();department.IsActive=true;await db.SaveChangesAsync();
            }
        }
        var history=await Use(c=>c.MessagesAsync(s.MemberId,long.Parse(group.Id),null,null,50,default));Assert.Empty(history.Items);
    }
}
