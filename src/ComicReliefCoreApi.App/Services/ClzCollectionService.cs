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
        using var reader = new StreamReader(csvStream);
        var rows = ClzCsvParser.ParseAndAggregate(reader, DateTime.UtcNow);
        return await _store.ReplaceAllAsync(rows, ct);
    }

    public Task<ClzImportStatus> GetStatusAsync(CancellationToken ct = default) => _store.GetStatusAsync(ct);

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
