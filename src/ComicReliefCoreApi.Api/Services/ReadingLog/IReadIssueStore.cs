using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.Api.Services.ReadingLog;

public interface IReadIssueStore
{
    /// <summary>Logs one read issue. readAt null for a backfilled entry with no known real date - see ReadIssue's own docs on why insertion order still carries the real reading order regardless.</summary>
    Task AddAsync(string series, string normalizedSeries, int issueNumber, DateTime? readAt, CancellationToken ct = default);

    /// <summary>Bulk-inserts a backfill list in the given order, all with readAt null - see AddAsync.</summary>
    Task<int> BulkAddAsync(IReadOnlyList<(string Series, string NormalizedSeries, int IssueNumber)> entries, CancellationToken ct = default);

    /// <summary>True if this (series, issue) has been logged as read at least once.</summary>
    Task<bool> IsReadAsync(string normalizedSeries, int issueNumber, CancellationToken ct = default);

    /// <summary>Every (NormalizedSeries, IssueNumber) pair ever logged read, for bulk cross-referencing (e.g. marking a same-week list's already-read items) without one query per item.</summary>
    Task<IReadOnlySet<(string NormalizedSeries, int IssueNumber)>> GetAllReadKeysAsync(CancellationToken ct = default);

    /// <summary>Most recently logged reads, newest first by Id (insertion order - see ReadIssue's own docs).</summary>
    Task<IReadOnlyList<ReadIssue>> GetRecentAsync(int max, CancellationToken ct = default);
}
