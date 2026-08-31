using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.Api.Services.Clz;

public record ClzImportStatus(bool HasData, int SeriesCount, DateTime? ImportedAt);

public interface IClzImportStore
{
    /// <summary>Wipes all existing rows and inserts the new set - a CLZ export is always a full collection snapshot, so "refresh" means replace, not merge.</summary>
    Task<int> ReplaceAllAsync(IReadOnlyList<ClzSeriesSummary> rows, CancellationToken ct = default);

    Task<ClzImportStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Keyed by NormalizedSeries, for exact-match lookup against PullListEntry.NormalizedTitle.</summary>
    Task<IReadOnlyDictionary<string, ClzSeriesSummary>> GetAllByNormalizedSeriesAsync(CancellationToken ct = default);
}
