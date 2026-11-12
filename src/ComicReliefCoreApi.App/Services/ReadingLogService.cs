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
        return owned.Select(o => ToView(o, readKeys)).ToList();
    }

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

        return owned
            .GroupBy(o => o.NormalizedSeries)
            .Select(g =>
            {
                // Lowest-numbered owned issue not yet read - assumes issues get read in
                // order, same assumption the rest of this feature already makes. Null (no
                // row) when every owned issue of this series is already read.
                var nextUnread = g
                    .Where(o => !readKeys.Contains((o.NormalizedSeries, o.IssueNumber)))
                    .OrderBy(o => o.IssueNumber)
                    .FirstOrDefault();
                return new SeriesSuggestion(g.First().Series, nextUnread?.IssueNumber, nextUnread?.ReleaseDate);
            })
            .OrderBy(s => s.Series)
            .ToList();
    }

    private static OwnedIssueView ToView(ClzIssueRelease issue, IReadOnlySet<(string NormalizedSeries, int IssueNumber)> readKeys) =>
        new(issue.Series, issue.IssueNumber, issue.ReleaseDate, readKeys.Contains((issue.NormalizedSeries, issue.IssueNumber)));
}
