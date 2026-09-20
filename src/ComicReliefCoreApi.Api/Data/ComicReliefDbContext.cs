using ComicReliefCoreApi.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ComicReliefCoreApi.Api.Data;

public class ComicReliefDbContext : DbContext
{
    public ComicReliefDbContext(DbContextOptions<ComicReliefDbContext> options) : base(options)
    {
    }

    public DbSet<PullListEntry> PullListEntries => Set<PullListEntry>();
    public DbSet<PullListAddAttempt> PullListAddAttempts => Set<PullListAddAttempt>();
    public DbSet<DcbsSession> DcbsSessions => Set<DcbsSession>();
    public DbSet<ClzSeriesSummary> ClzSeriesSummaries => Set<ClzSeriesSummary>();
    public DbSet<DcbsSolicitationEntry> DcbsSolicitationEntries => Set<DcbsSolicitationEntry>();
    public DbSet<DcbsOrderSnapshotLine> DcbsOrderSnapshotLines => Set<DcbsOrderSnapshotLine>();
    public DbSet<ClzIssueRelease> ClzIssueReleases => Set<ClzIssueRelease>();
    public DbSet<ReadIssue> ReadIssues => Set<ReadIssue>();
    public DbSet<ReviewFlagEntry> ReviewFlagEntries => Set<ReviewFlagEntry>();

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

        modelBuilder.Entity<ClzSeriesSummary>(entity =>
        {
            entity.HasIndex(e => e.NormalizedSeries).IsUnique();
        });

        modelBuilder.Entity<DcbsSolicitationEntry>(entity =>
        {
            entity.HasIndex(e => e.Publisher);
        });

        // SQLite has no timezone-aware datetime type, so EF reads every DateTime back with
        // Kind=Unspecified regardless of what was written - System.Text.Json then serializes
        // it without a trailing "Z"/offset, and a browser's `new Date(...)` treats an
        // offset-less ISO string as LOCAL time rather than UTC. Every DateTime in this app is
        // UTC by convention (DateTime.UtcNow throughout), so every displayed timestamp
        // (imported/synced/refreshed/etc.) was silently mis-converted - confirmed live this
        // session as "the upload time shows in UTC" when it should show local. Forcing
        // Kind=Utc back on read fixes serialization (and therefore every toLocaleString() call
        // across the whole app) in one place instead of patching each JS call site.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableUtcConverter);
                }
            }
        }
    }
}
