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

    /// <summary>Same replace-wholesale semantics as ReplaceAllAsync, but for the per-issue rows (see ClzIssueRelease) - a separate table from the per-series aggregate, populated from the same upload.</summary>
    Task<int> ReplaceAllIssuesAsync(IReadOnlyList<ClzIssueRelease> rows, CancellationToken ct = default);

    /// <summary>Every per-issue row on file, for in-memory matching against a shipment's lines (which need series+issue-number matching, not just a series-name lookup).</summary>
    Task<IReadOnlyList<ClzIssueRelease>> GetAllIssuesAsync(CancellationToken ct = default);
}
