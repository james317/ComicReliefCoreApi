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

        var sameWeek = await _clzStore.GetIssuesByReleaseDateAsync(releaseDate, ct);
        var readKeys = await _readStore.GetAllReadKeysAsync(ct);
        return new ReadingLogWeekView(
            target.Series, target.IssueNumber, releaseDate, sameWeek.Select(o => ToView(o, readKeys)).ToList());
    }

    public Task<IReadOnlyList<ReadIssue>> GetRecentlyReadAsync(int max, CancellationToken ct = default) =>
        _readStore.GetRecentAsync(max, ct);

    private static OwnedIssueView ToView(ClzIssueRelease issue, IReadOnlySet<(string NormalizedSeries, int IssueNumber)> readKeys) =>
        new(issue.Series, issue.IssueNumber, issue.ReleaseDate, readKeys.Contains((issue.NormalizedSeries, issue.IssueNumber)));
}
