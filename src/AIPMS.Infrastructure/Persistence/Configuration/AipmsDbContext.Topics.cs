using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureTopics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectTopic>(e =>
        {
            e.ToTable("project_topics"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectPeriodId).HasColumnName("project_period_id");
            e.Property(x => x.LeadDepartmentId).HasColumnName("lead_department_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(300);
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
            e.Property(x => x.ProblemStatement).HasColumnName("problem_statement").HasMaxLength(4000);
            e.Property(x => x.Objectives).HasColumnName("objectives").HasMaxLength(4000);
            e.Property(x => x.ExpectedOutput).HasColumnName("expected_output").HasMaxLength(4000);
            e.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(200);
            e.Property(x => x.TechnologiesJson).HasColumnName("technologies_json");
            e.Property(x => x.KeywordsJson).HasColumnName("keywords_json");
            e.Property(x => x.ProjectMode).HasColumnName("project_mode").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.PrimaryMajorId).HasColumnName("primary_major_id");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            e.Property(x => x.PublishedBy).HasColumnName("published_by");
            e.Property(x => x.PublishedAt).HasColumnName("published_at").HasPrecision(0);
            e.Property(x => x.ClosedBy).HasColumnName("closed_by");
            e.Property(x => x.ClosedAt).HasColumnName("closed_at").HasPrecision(0);
            e.Property(x => x.CloseReason).HasColumnName("close_reason").HasMaxLength(2000);
            e.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectPeriodId, x.Code }).IsUnique().HasDatabaseName("uq_project_topics_period_code");
            e.HasIndex(x => new { x.ProjectPeriodId, x.Status, x.Id });
            e.HasOne(x => x.Period).WithMany().HasForeignKey(x => x.ProjectPeriodId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.LeadDepartment).WithMany().HasForeignKey(x => x.LeadDepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Major>().WithMany().HasForeignKey(x => x.PrimaryMajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.PublishedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ClosedBy).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(x => x.Requirements).WithOne().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<TopicMajorRequirement>(e =>
        {
            e.ToTable("topic_major_requirements"); e.HasKey(x => new { x.TopicId, x.MajorId });
            e.Property(x => x.TopicId).HasColumnName("topic_id");
            e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.MinMembers).HasColumnName("min_members");
            e.Property(x => x.MaxMembers).HasColumnName("max_members");
            e.Property(x => x.Responsibility).HasColumnName("responsibility").HasMaxLength(1000);
            e.HasIndex(x => new { x.MajorId, x.TopicId });
            e.HasIndex(x => new { x.DepartmentId, x.TopicId });
            e.HasOne(x => x.Major).WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Project>(e =>
        {
            e.Property(x => x.TopicId).HasColumnName("topic_id");
            e.Property(x => x.ProposalSource).HasColumnName("proposal_source").HasMaxLength(30).IsUnicode(false).HasDefaultValue("STUDENT_PROPOSAL");
            e.HasOne(x => x.Topic).WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.TopicId).HasDatabaseName("ix_projects_topic_id");
        });
    }
}
