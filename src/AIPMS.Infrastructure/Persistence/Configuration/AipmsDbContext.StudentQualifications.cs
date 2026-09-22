using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureStudentQualifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StudentQualification>(entity =>
        {
            entity.ToTable("student_qualifications", table =>
            {
                table.HasCheckConstraint("ck_student_qualifications_training_status",
                    "[training_status] IN ('PENDING_TRAINING','TRAINING_COMPLETED')");
                table.HasCheckConstraint("ck_student_qualifications_verification_status",
                    "[verification_status] IN ('PENDING_VERIFICATION','VERIFIED','REJECTED','EXPIRED')");
            });
            entity.HasKey(x => x.Id).HasName("pk_student_qualifications");
            entity.HasIndex(x => new { x.UserId, x.QualificationType })
                .IsUnique().HasDatabaseName("uq_student_qualifications_user_type");
            entity.HasIndex(x => new { x.OrganizationId, x.VerificationStatus, x.UpdatedAt })
                .HasDatabaseName("ix_student_qualifications_org_status_updated");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.QualificationType).HasColumnName("qualification_type").HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.TrainingStatus).HasColumnName("training_status").HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.VerificationStatus).HasColumnName("verification_status").HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.CertificateNumber).HasColumnName("certificate_number").HasMaxLength(100);
            entity.Property(x => x.CertificateFileId).HasColumnName("certificate_file_id");
            entity.Property(x => x.IssuedAt).HasColumnName("issued_at").HasPrecision(0);
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasPrecision(0);
            entity.Property(x => x.VerifiedBy).HasColumnName("verified_by");
            entity.Property(x => x.VerifiedAt).HasColumnName("verified_at").HasPrecision(0);
            entity.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
            entity.Property(x => x.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0);
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            entity.HasOne<Models.User>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_student_qualifications_user");
            entity.HasOne<Models.Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_student_qualifications_organization");
            entity.HasOne<Models.User>().WithMany().HasForeignKey(x => x.VerifiedBy)
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_student_qualifications_verified_by");
            entity.HasOne<Models.File>().WithMany().HasForeignKey(x => x.CertificateFileId)
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_student_qualifications_certificate_file");
        });

        modelBuilder.Entity<ProjectPeriodQualificationPolicy>(entity =>
        {
            entity.ToTable("project_period_qualification_policies");
            entity.HasKey(x => x.ProjectPeriodId).HasName("pk_project_period_qualification_policies");
            entity.Property(x => x.ProjectPeriodId).HasColumnName("project_period_id").ValueGeneratedNever();
            entity.Property(x => x.RequireStudentQualification).HasColumnName("require_student_qualification");
            entity.Property(x => x.QualificationType).HasColumnName("qualification_type").HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.RequireCertificate).HasColumnName("require_certificate");
            entity.Property(x => x.CheckExpiration).HasColumnName("check_expiration");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0);
            entity.HasOne<Models.ProjectPeriod>().WithOne().HasForeignKey<ProjectPeriodQualificationPolicy>(x => x.ProjectPeriodId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_project_period_qualification_policy_period");
        });
    }
}
