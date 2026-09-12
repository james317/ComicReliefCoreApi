using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Services.Clz;

namespace ComicReliefCoreApi.App.Services;

public interface IClzCollectionService
{
    /// <summary>Parses a full CLZ collection export and replaces the stored collection snapshot entirely. Returns the number of distinct series imported. Never call this with a shipment-scoped export - use ImportShipmentIssuesAsync instead, or every other series' history gets wiped.</summary>
    Task<int> ImportAsync(Stream csvStream, CancellationToken ct = default);

    /// <summary>Parses a shipment-scoped CLZ export (just the issues in one box) and merges its per-issue release dates in - safe to call as often as you like, never touches the full-collection snapshot ImportAsync maintains.</summary>
    Task<int> ImportShipmentIssuesAsync(Stream csvStream, CancellationToken ct = default);

    Task<ClzImportStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// The last-purchased ship date (not an issue number) for each given normalized
    /// pull-list title, matched by exact normalized-title equality only - deliberately
    /// not fuzzy/substring matching.
    /// A quick substring-based cross-reference attempt this session produced real false
    /// positives (e.g. "Batman" matching an unrelated one-shot "Archie Meets Batman 66"
    /// just because the words overlap), so titles with no exact CLZ match are simply
    /// absent from the result rather than guessed at.
    /// </summary>
    Task<IReadOnlyDictionary<string, DateOnly?>> GetLastKnownIssueDatesAsync(
        IEnumerable<string> normalizedTitles, CancellationToken ct = default);

    /// <summary>Every per-issue release-date row on file (see ClzIssueRelease), for matching a shipment's lines to when each issue actually came out.</summary>
    Task<IReadOnlyList<ClzIssueRelease>> GetAllIssueReleasesAsync(CancellationToken ct = default);
}
