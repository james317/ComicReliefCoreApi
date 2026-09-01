using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.App.Services;

/// <summary>One listing item plus which publisher category it was crawled from.</summary>
public record SolicitationItem(string Publisher, DcbsListingItem Item);

/// <summary>Every current-solicitation item matched to one pull-list entry.</summary>
public record SolicitationMatch(int PullListEntryId, string PullListTitle, PullListStatus Status, IReadOnlyList<SolicitationItem> Items);

public record SolicitationCandidateList(
    DateTime? GeneratedAt,
    IReadOnlyList<SolicitationMatch> TrackedMatches,
    IReadOnlyList<SolicitationItem> Untracked);

public record SolicitationCacheStatus(
    DateTime? LastRefreshedAt,
    int TotalItems,
    IReadOnlyDictionary<string, int> PublisherItemCounts,
    IReadOnlyDictionary<string, string> PublisherErrors);

/// <summary>
/// Crawls every non-manga DCBS publisher category (one request each, see
/// DcbsPublisherCategories) and caches the result in memory - refreshed on demand, not on
/// every request, since a full crawl hits ~20 real DCBS pages. Cross-referencing against
/// the pull list is pure/stateless (BuildCandidateList) so callers pass in whatever pull-
/// list entries they've already loaded rather than this service touching the database
/// itself - keeps a Singleton (needed so the cache survives across requests) from ever
/// holding a scoped DbContext.
/// </summary>
public interface ISolicitationService
{
    Task<SolicitationCacheStatus> RefreshAsync(CancellationToken ct = default);

    SolicitationCacheStatus GetStatus();

    SolicitationCandidateList BuildCandidateList(IReadOnlyCollection<PullListEntry> trackedEntries);
}
