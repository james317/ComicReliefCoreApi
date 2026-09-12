using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Services.Clz;

namespace ComicReliefCoreApi.App.Services;

public class ClzCollectionService : IClzCollectionService
{
    private readonly IClzImportStore _store;

    public ClzCollectionService(IClzImportStore store)
    {
        _store = store;
    }

    public async Task<int> ImportAsync(Stream csvStream, CancellationToken ct = default)
    {
        // Read once, parse twice (the per-series aggregate and the per-issue rows are two
        // different shapes over the same CSV) - a Stream can only be consumed once, so the
        // second pass needs its own reader over a buffered copy of the same text.
        using var streamReader = new StreamReader(csvStream);
        var csvText = await streamReader.ReadToEndAsync(ct);
        var importedAt = DateTime.UtcNow;

        var seriesRows = ClzCsvParser.ParseAndAggregate(new StringReader(csvText), importedAt);
        var issueRows = ClzCsvParser.ParsePerIssueRows(new StringReader(csvText), importedAt);

        var count = await _store.ReplaceAllAsync(seriesRows, ct);
        await _store.ReplaceAllIssuesAsync(issueRows, ct);
        return count;
    }

    public Task<ClzImportStatus> GetStatusAsync(CancellationToken ct = default) => _store.GetStatusAsync(ct);

    public Task<IReadOnlyList<ClzIssueRelease>> GetAllIssueReleasesAsync(CancellationToken ct = default) =>
        _store.GetAllIssuesAsync(ct);

    public async Task<IReadOnlyDictionary<string, DateOnly?>> GetLastKnownIssueDatesAsync(
        IEnumerable<string> normalizedTitles, CancellationToken ct = default)
    {
        var bySeries = await _store.GetAllByNormalizedSeriesAsync(ct);
        var result = new Dictionary<string, DateOnly?>();
        foreach (var title in normalizedTitles)
        {
            if (bySeries.TryGetValue(title, out var summary))
            {
                result[title] = summary.LastReleaseDate;
            }
        }
        return result;
    }
}
