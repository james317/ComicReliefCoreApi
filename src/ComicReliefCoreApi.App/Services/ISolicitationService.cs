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
    IReadOnlyDictionary<string, int> PublisherItemCounts);

/// <summary>Errors are only ever about the refresh call that just ran - never persisted, since a failed publisher just leaves its last-known-good rows in place (see IDcbsSolicitationStore).</summary>
public record SolicitationRefreshResult(SolicitationCacheStatus Status, IReadOnlyDictionary<string, string> PublisherErrors);

/// <summary>
/// Crawls every non-manga DCBS publisher category (one request each, see
/// DcbsPublisherCategories) and persists the result via IDcbsSolicitationStore - refreshed
/// on demand, not on every request or server restart, since a full crawl hits ~20 real DCBS
/// pages and the underlying catalog itself only turns over about once a month. Cross-
/// referencing against the pull list is otherwise the only logic here; callers pass in
/// whatever pull-list entries they've already loaded rather than this service querying the
/// database for them directly.
/// </summary>
public interface ISolicitationService
{
    Task<SolicitationRefreshResult> RefreshAsync(CancellationToken ct = default);

    Task<SolicitationCacheStatus> GetStatusAsync(CancellationToken ct = default);

    Task<SolicitationCandidateList> BuildCandidateListAsync(
        IReadOnlyCollection<PullListEntry> trackedEntries, CancellationToken ct = default);
}
