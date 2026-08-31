using ComicReliefCoreApi.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Api.Data;

public class ComicReliefDbContext : DbContext
{
    public ComicReliefDbContext(DbContextOptions<ComicReliefDbContext> options) : base(options)
    {
    }

    public DbSet<PullListEntry> PullListEntries => Set<PullListEntry>();
    public DbSet<PullListAddAttempt> PullListAddAttempts => Set<PullListAddAttempt>();
    public DbSet<DcbsSession> DcbsSessions => Set<DcbsSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PullListEntry>(entity =>
        {
            entity.HasIndex(e => e.NormalizedTitle).IsUnique();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.PreferredFormat).HasConversion<string>();
            entity.Property(e => e.LastSuccessfulMethod).HasConversion<string>();

            entity.HasMany(e => e.Attempts)
                .WithOne(a => a.PullListEntry)
                .HasForeignKey(a => a.PullListEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PullListAddAttempt>(entity =>
        {
            entity.Property(a => a.Method).HasConversion<string>();
        });
    }
}
