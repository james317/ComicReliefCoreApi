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

    /// <summary>Everything on file with this exact release date - the same view as GetSameWeekAsync, but reached by date (stepping to the previous/next ship date) rather than anchored on one issue, so Series/IssueNumber are null.</summary>
    Task<ReadingLogWeekView> GetWeekAsync(DateOnly releaseDate, CancellationToken ct = default);

    Task<IReadOnlyList<ReadIssue>> GetRecentlyReadAsync(int max, CancellationToken ct = default);

    /// <summary>Removes a mis-attributed read entry (e.g. logged under the wrong series name) rather than living with it. Returns how many rows were removed.</summary>
    Task<int> DeleteReadAsync(string series, int issueNumber, CancellationToken ct = default);

    /// <summary>
    /// Type-ahead suggestions for the search box: one row per matching series (not one per
    /// issue, unlike SearchOwnedIssuesAsync), each annotated with the next issue to read - the
    /// first owned unread issue after the highest one marked read (or the lowest owned issue if
    /// none is), so a series started mid-run doesn't point back at its oldest issue - plus any
    /// skipped issues inside the read range (see OwnedIssueView). Null when caught up.
    /// </summary>
    Task<IReadOnlyList<SeriesSuggestion>> SuggestSeriesAsync(string query, CancellationToken ct = default);
}

/// <summary>
/// One owned issue. A series' reading range runs from the lowest to the highest issue marked
/// read - issues before the first one recorded are where the user started, not skips, since
/// nothing was being logged yet. IsSkipped: unread, inside that range (a real skip). IsEarlier:
/// before the latest read issue and not a skip - read already, or from before reading started -
/// so it can be tucked away to put the next unread issue up front.
/// </summary>
public sealed record OwnedIssueView(
    string Series, int IssueNumber, DateOnly? ReleaseDate, bool AlreadyRead, bool IsSkipped = false, bool IsEarlier = false);

public sealed record ReadingLogWeekView(
    string? Series,
    int? IssueNumber,
    DateOnly ReleaseDate,
    IReadOnlyList<OwnedIssueView> SameWeekIssues,
    DateOnly? PreviousReleaseDate,
    DateOnly? NextReleaseDate);

public sealed record SeriesSuggestion(
    string Series, int? NextUnreadIssueNumber, DateOnly? NextUnreadReleaseDate, IReadOnlyList<int> SkippedIssueNumbers);
