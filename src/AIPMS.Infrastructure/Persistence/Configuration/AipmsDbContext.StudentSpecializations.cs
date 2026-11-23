using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    public virtual DbSet<DepartmentSpecialization> DepartmentSpecializations { get; set; } = null!;

    private static void ConfigureStudentSpecializations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DepartmentSpecialization>(e =>
        {
            e.ToTable("department_specializations");
            e.HasKey(x => x.Id).HasName("pk_department_specializations");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DepartmentId).HasColumnName("department_id");
            e.Property(x => x.MajorId).HasColumnName("major_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(50);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasPrecision(0).HasDefaultValueSql("(sysutcdatetime())");
            e.HasIndex(x => new { x.DepartmentId, x.Code }).IsUnique().HasDatabaseName("uq_department_specializations_dept_code");
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Major).WithMany().HasForeignKey(x => x.MajorId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<M.User>(e =>
        {
            e.Property(x => x.ClassCode).HasColumnName("class_code").HasMaxLength(50);
            e.Property(x => x.SpecializationId).HasColumnName("specialization_id");
            e.HasOne(x => x.Specialization).WithMany().HasForeignKey(x => x.SpecializationId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
