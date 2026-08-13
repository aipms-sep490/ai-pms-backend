using AIPMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence;

public sealed class AipmsDbContext(DbContextOptions<AipmsDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AipmsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
