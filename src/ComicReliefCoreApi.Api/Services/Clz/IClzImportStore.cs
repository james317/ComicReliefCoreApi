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

    /// <summary>
    /// Merges by (NormalizedSeries, IssueNumber) - deliberately NOT a wholesale replace like
    /// ReplaceAllAsync, because this table has to accept both a full collection export and a
    /// shipment-scoped export (just the handful of issues in one box) safely. A wholesale
    /// replace from a shipment-scoped upload would wipe every other series' history - this
    /// happened live once before this method existed (see ClzImportStore for the incident).
    /// </summary>
    Task<int> UpsertIssuesAsync(IReadOnlyList<ClzIssueRelease> rows, CancellationToken ct = default);

    /// <summary>Every per-issue row on file, for in-memory matching against a shipment's lines (which need series+issue-number matching, not just a series-name lookup).</summary>
    Task<IReadOnlyList<ClzIssueRelease>> GetAllIssuesAsync(CancellationToken ct = default);

    /// <summary>
    /// Directly sets one (NormalizedSeries, IssueNumber) row's release date - creates it if
    /// missing, updates it in place if present. For the handful of items CLZ never gets a
    /// clean date for on its own (no catalog record at all, or an unhelpful one like an
    /// original-printing date on a reprint) and have to be looked up and entered by hand
    /// instead of coming from a CSV upload.
    /// </summary>
    Task SetIssueReleaseDateAsync(string series, string normalizedSeries, int issueNumber, DateOnly? releaseDate, CancellationToken ct = default);

    /// <summary>Case-insensitive substring search over Series, for a human to pick the right owned issue from - deliberately permissive (unlike IsLikelySeriesMatch's strict matching for automated cross-referencing) since a person visually confirms the result before recording a read.</summary>
    Task<IReadOnlyList<ClzIssueRelease>> SearchBySeriesAsync(string query, CancellationToken ct = default);

    /// <summary>Every ClzIssueRelease row sharing this exact release date, for "what else shipped the same week" - DCBS's own release-date granularity already buckets by weekly ship day, so exact-date equality is the same thing as "same week."</summary>
    Task<IReadOnlyList<ClzIssueRelease>> GetIssuesByReleaseDateAsync(DateOnly releaseDate, CancellationToken ct = default);

    /// <summary>One specific (NormalizedSeries, IssueNumber) row, or null if not on file.</summary>
    Task<ClzIssueRelease?> GetIssueAsync(string normalizedSeries, int issueNumber, CancellationToken ct = default);
}
