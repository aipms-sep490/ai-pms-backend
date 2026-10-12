using System.Text.RegularExpressions;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureChat(ModelBuilder model)
    {
        model.Entity<ChatConversation>().ToTable("chat_conversations");
        model.Entity<ChatMembershipInterval>().ToTable("chat_membership_intervals");
        model.Entity<ChatMessage>().ToTable("chat_messages");
        model.Entity<ChatMemberState>().ToTable("chat_member_state").HasKey(x => new { x.ConversationId, x.UserId });
        model.Entity<ChatOutbox>().ToTable("chat_outbox");
        model.Entity<ChatScopeMember>().HasNoKey().ToView("chat_scope_members");
        model.Entity<ChatUpload>().ToTable("chat_uploads");
        model.Entity<ChatMessageAttachment>().ToTable("chat_message_attachments").HasKey(x => new { x.MessageId, x.UploadId });
        model.Entity<ChatMessageAttachment>().HasIndex(x => x.UploadId).IsUnique();
        model.Entity<ChatReaction>().ToTable("chat_reactions").HasKey(x => new { x.MessageId, x.UserId, x.Emoji });
        model.Entity<ChatReaction>().Property(x => x.Emoji).HasMaxLength(32);
        foreach (var type in new[] { typeof(ChatConversation), typeof(ChatMembershipInterval), typeof(ChatMessage), typeof(ChatMemberState), typeof(ChatOutbox), typeof(ChatScopeMember), typeof(ChatUpload), typeof(ChatMessageAttachment), typeof(ChatReaction) })
        foreach (var property in model.Entity(type).Metadata.GetProperties())
            property.SetColumnName(Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
        model.Entity<ChatConversation>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        model.Entity<ChatMessage>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        model.Entity<ChatConversation>().HasIndex(x => new { x.FirstUserId, x.SecondUserId }).IsUnique().HasFilter("kind='DIRECT'");
        model.Entity<ChatMembershipInterval>().HasIndex(x => new { x.ConversationId, x.UserId }).IsUnique().HasFilter("left_at IS NULL");
        model.Entity<ChatMessage>().HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
        model.Entity<ChatMessage>().HasIndex(x => new { x.ConversationId, x.SenderId, x.ClientMessageId }).IsUnique();
        // SQL source triggers invalidate chat intervals in the same business transaction.
        model.Entity<TeamMember>().ToTable("team_members", t => t.UseSqlOutputClause(false));
        model.Entity<SupervisorAssignment>().ToTable("supervisor_assignments", t => t.UseSqlOutputClause(false));
        model.Entity<User>().ToTable("users", t => t.UseSqlOutputClause(false));
        model.Entity<UserRole>().ToTable("user_roles", t => t.UseSqlOutputClause(false));
        model.Entity<Project>().ToTable("projects", t => t.UseSqlOutputClause(false));
        model.Entity<Team>().ToTable("teams", t => t.UseSqlOutputClause(false));
        model.Entity<Department>().ToTable("departments", t => t.UseSqlOutputClause(false));
        model.Entity<Organization>().ToTable("organizations", t => t.UseSqlOutputClause(false));
    }
}
