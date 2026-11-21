using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Services;
using ComicReliefCoreApi.Api.Services.Clz;
using ComicReliefCoreApi.Api.Services.ReadingLog;

namespace ComicReliefCoreApi.App.Services;

public class ReadingLogService : IReadingLogService
{
    private readonly IClzImportStore _clzStore;
    private readonly IReadIssueStore _readStore;

    public ReadingLogService(IClzImportStore clzStore, IReadIssueStore readStore)
    {
        _clzStore = clzStore;
        _readStore = readStore;
    }

    public async Task<IReadOnlyList<OwnedIssueView>> SearchOwnedIssuesAsync(string query, CancellationToken ct = default)
    {
        var owned = await _clzStore.SearchBySeriesAsync(query, ct);
        var readKeys = await _readStore.GetAllReadKeysAsync(ct);
        var ranges = ReadRanges(readKeys);
        return owned.Select(o =>
        {
            var view = ToView(o, readKeys);
            if (!ranges.TryGetValue(o.NormalizedSeries, out var range))
            {
                return view;
            }

            var skipped = !view.AlreadyRead && o.IssueNumber > range.First && o.IssueNumber < range.Last;
            return view with { IsSkipped = skipped, IsEarlier = o.IssueNumber < range.Last && !skipped };
        }).ToList();
    }

    /// <summary>
    /// Per series, the lowest and highest issue number marked read. The lowest is where the
    /// user started logging that series (11/21/2026 decision: older issues are neither read nor
    /// skipped, just from before the log - so no backfill is needed to make "next" sensible).
    /// Marking an older issue read moves the start back automatically.
    /// </summary>
    private static Dictionary<string, (int First, int Last)> ReadRanges(
        IReadOnlySet<(string NormalizedSeries, int IssueNumber)> readKeys) =>
        readKeys
            .GroupBy(k => k.NormalizedSeries)
            .ToDictionary(g => g.Key, g => (g.Min(k => k.IssueNumber), g.Max(k => k.IssueNumber)));

    public Task RecordReadAsync(string series, int issueNumber, CancellationToken ct = default)
    {
        return _readStore.AddAsync(series, TitleNormalizer.Normalize(series), issueNumber, DateTime.UtcNow, ct);
    }

    public Task<int> BackfillAsync(IReadOnlyList<(string Series, int IssueNumber)> entries, CancellationToken ct = default)
    {
        return _readStore.BulkAddAsync(
            entries.Select(e => (e.Series, TitleNormalizer.Normalize(e.Series), e.IssueNumber)).ToList(), ct);
    }

    public async Task<ReadingLogWeekView?> GetSameWeekAsync(string series, int issueNumber, CancellationToken ct = default)
    {
        var normalizedSeries = TitleNormalizer.Normalize(series);
        var target = await _clzStore.GetIssueAsync(normalizedSeries, issueNumber, ct);
        if (target?.ReleaseDate is not { } releaseDate)
        {
            return null;
        }

        return await BuildWeekViewAsync(target.Series, target.IssueNumber, releaseDate, ct);
    }

    public Task<ReadingLogWeekView> GetWeekAsync(DateOnly releaseDate, CancellationToken ct = default) =>
        BuildWeekViewAsync(null, null, releaseDate, ct);

    private async Task<ReadingLogWeekView> BuildWeekViewAsync(
        string? series, int? issueNumber, DateOnly releaseDate, CancellationToken ct)
    {
        var sameWeek = await _clzStore.GetIssuesByReleaseDateAsync(releaseDate, ct);
        var readKeys = await _readStore.GetAllReadKeysAsync(ct);
        var (previous, next) = await _clzStore.GetAdjacentReleaseDatesAsync(releaseDate, ct);
        return new ReadingLogWeekView(
            series, issueNumber, releaseDate, sameWeek.Select(o => ToView(o, readKeys)).ToList(), previous, next);
    }

    public Task<IReadOnlyList<ReadIssue>> GetRecentlyReadAsync(int max, CancellationToken ct = default) =>
        _readStore.GetRecentAsync(max, ct);

    public Task<int> DeleteReadAsync(string series, int issueNumber, CancellationToken ct = default) =>
        _readStore.DeleteAsync(TitleNormalizer.Normalize(series), issueNumber, ct);

    public async Task<IReadOnlyList<SeriesSuggestion>> SuggestSeriesAsync(string query, CancellationToken ct = default)
    {
        var owned = await _clzStore.SearchBySeriesAsync(query, ct);
        var readKeys = await _readStore.GetAllReadKeysAsync(ct);
        var ranges = ReadRanges(readKeys);

        return owned
            .GroupBy(o => o.NormalizedSeries)
            .Select(g =>
            {
                var unread = g
                    .Where(o => !readKeys.Contains((o.NormalizedSeries, o.IssueNumber)))
                    .OrderBy(o => o.IssueNumber)
                    .ToList();
                if (!ranges.TryGetValue(g.Key, out var range))
                {
                    // Nothing logged yet: start at the lowest owned issue.
                    var first = unread.FirstOrDefault();
                    return new SeriesSuggestion(g.First().Series, first?.IssueNumber, first?.ReleaseDate, Array.Empty<int>());
                }

                // Next = first unread after the latest read issue (assumes in-order reading,
                // same as the rest of this feature); skips = unread holes inside the read range.
                var next = unread.FirstOrDefault(o => o.IssueNumber > range.Last);
                var skipped = unread
                    .Where(o => o.IssueNumber > range.First && o.IssueNumber < range.Last)
                    .Select(o => o.IssueNumber)
                    .Distinct()
                    .ToList();
                return new SeriesSuggestion(g.First().Series, next?.IssueNumber, next?.ReleaseDate, skipped);
            })
            .OrderBy(s => s.Series)
            .ToList();
    }

    private static OwnedIssueView ToView(ClzIssueRelease issue, IReadOnlySet<(string NormalizedSeries, int IssueNumber)> readKeys) =>
        new(issue.Series, issue.IssueNumber, issue.ReleaseDate, readKeys.Contains((issue.NormalizedSeries, issue.IssueNumber)));
}
