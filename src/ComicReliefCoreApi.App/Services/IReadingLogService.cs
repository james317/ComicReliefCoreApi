using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.App.Services;

public interface IReadingLogService
{
    /// <summary>Case-insensitive series search over owned issues (see IClzImportStore.SearchBySeriesAsync), each flagged with whether it's already been logged read.</summary>
    Task<IReadOnlyList<OwnedIssueView>> SearchOwnedIssuesAsync(string query, CancellationToken ct = default);

    /// <summary>Logs one issue as read right now.</summary>
    Task RecordReadAsync(string series, int issueNumber, CancellationToken ct = default);

    /// <summary>
    /// Backfills a list of already-read issues with no known real dates, in the given order -
    /// see ReadIssue's own docs on why insertion order alone still carries real reading order.
    /// </summary>
    Task<int> BackfillAsync(IReadOnlyList<(string Series, int IssueNumber)> entries, CancellationToken ct = default);

    /// <summary>
    /// Everything else on file that shares this issue's exact release date (see
    /// GetIssuesByReleaseDateAsync for why exact-date equality is "same week" here), each
    /// flagged with whether it's already been read - the guard against reading out of
    /// release order. Returns null if the issue itself has no release date on file (nothing
    /// to compare against).
    /// </summary>
    Task<ReadingLogWeekView?> GetSameWeekAsync(string series, int issueNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ReadIssue>> GetRecentlyReadAsync(int max, CancellationToken ct = default);
}

public sealed record OwnedIssueView(string Series, int IssueNumber, DateOnly? ReleaseDate, bool AlreadyRead);

public sealed record ReadingLogWeekView(string Series, int IssueNumber, DateOnly ReleaseDate, IReadOnlyList<OwnedIssueView> SameWeekIssues);
