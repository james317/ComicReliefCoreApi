using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Api.Services.ReadingLog;

public class ReadIssueStore : IReadIssueStore
{
    private readonly ComicReliefDbContext _db;

    public ReadIssueStore(ComicReliefDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(string series, string normalizedSeries, int issueNumber, DateTime? readAt, CancellationToken ct = default)
    {
        _db.ReadIssues.Add(new ReadIssue
        {
            Series = series,
            NormalizedSeries = normalizedSeries,
            IssueNumber = issueNumber,
            ReadAt = readAt,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> BulkAddAsync(
        IReadOnlyList<(string Series, string NormalizedSeries, int IssueNumber)> entries, CancellationToken ct = default)
    {
        // AddRange (not one AddAsync call per entry) preserves the given list order as
        // consecutive auto-increment Ids - the actual reading order for a backfill with no
        // real per-entry dates.
        _db.ReadIssues.AddRange(entries.Select(e => new ReadIssue
        {
            Series = e.Series,
            NormalizedSeries = e.NormalizedSeries,
            IssueNumber = e.IssueNumber,
            ReadAt = null,
        }));
        await _db.SaveChangesAsync(ct);
        return entries.Count;
    }

    public async Task<bool> IsReadAsync(string normalizedSeries, int issueNumber, CancellationToken ct = default)
    {
        return await _db.ReadIssues.AnyAsync(r => r.NormalizedSeries == normalizedSeries && r.IssueNumber == issueNumber, ct);
    }

    public async Task<IReadOnlySet<(string NormalizedSeries, int IssueNumber)>> GetAllReadKeysAsync(CancellationToken ct = default)
    {
        var rows = await _db.ReadIssues.Select(r => new { r.NormalizedSeries, r.IssueNumber }).ToListAsync(ct);
        return rows.Select(r => (r.NormalizedSeries, r.IssueNumber)).ToHashSet();
    }

    public async Task<IReadOnlyList<ReadIssue>> GetRecentAsync(int max, CancellationToken ct = default)
    {
        // Backfilled history (ReadAt null) never counts as "recently read": a backfill is
        // inserted after everything already logged, so ordering by Id alone would put
        // issues read long ago at the top - e.g. Detective Comics #934-#1067, backfilled
        // 11/21/2026 behind real reads from that week.
        return await _db.ReadIssues
            .Where(r => r.ReadAt != null)
            .OrderByDescending(r => r.Id)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task<int> DeleteAsync(string normalizedSeries, int issueNumber, CancellationToken ct = default)
    {
        return await _db.ReadIssues
            .Where(r => r.NormalizedSeries == normalizedSeries && r.IssueNumber == issueNumber)
            .ExecuteDeleteAsync(ct);
    }
}
