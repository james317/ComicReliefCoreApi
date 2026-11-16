using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.App.Services;

/// <summary>
/// One listing item plus which publisher category it was crawled from. IsInLatestOrder is
/// a cross-reference against the synced order snapshot (IOrderSnapshotService), not a
/// scraped fact about the listing itself - true if this exact product code appears on the
/// user's most recently synced order. IsNewFirstIssueOrOneShot is a title-text
/// classification (issue number 1, or no issue number but an explicit "One-Shot" marker),
/// excluding facsimile/reprint editions - built as an independent, in-app "New #1s" view
/// (docs/BACKLOG.md "New-issue-#1 tracker") since DCBS's own equivalent listing has been
/// unreliable. FirstSeenAt is the raw fact (when this product code was first ever crawled -
/// see DcbsSolicitationEntry); IsNewSinceLastRefresh is the one derived boolean from it
/// (FirstSeenAt == this row's RefreshedAt), so a "what's new since I last checked" view
/// doesn't need its own separate timestamp tracking. Comparing FirstSeenAt against an order's
/// placed date for the "new since I built my order" view is left to the caller (Solicitations
/// page) - a plain date-range filter over already-fetched items, the same pattern already
/// used for the New #1s view's own client-side filter. FavoriteWriters/AvoidedWriters are the
/// writer name(s) (see CreatorCreditParser) that matched an entry on the tracked writer list
/// (IWriterPreferenceService) - empty when no writer credit on this item matches either list.
/// A name can only ever appear in one of the two lists, never both (WriterPreference.Type is
/// exclusive per normalized name). IsInCheckedCart: this item is in the last cart run through
/// Cart Check (ICheckedCartStore) - exact product code for a PDF cart, same series and issue
/// number for a pasted one (which has no codes). Regular: set when this item's series is one
/// the user buys regularly (see RegularPurchase) - null otherwise, and for items with no issue
/// number or that are facsimile/reprint editions.
/// </summary>
public record SolicitationItem(
    string Publisher, DcbsListingItem Item, bool IsInLatestOrder, bool IsNewFirstIssueOrOneShot,
    DateTime FirstSeenAt, bool IsNewSinceLastRefresh,
    IReadOnlyList<string> FavoriteWriters, IReadOnlyList<string> AvoidedWriters,
    bool IsInCheckedCart, RegularPurchase? Regular);

/// <summary>
/// "You buy this series": it appears in at least two of the user's last six ordering months
/// (MonthsBought of MonthsConsidered). Counted in months rather than orders so a bi-monthly
/// book still counts and a small add-on order doesn't use up the window. LastIssueNumber /
/// LastOrderDate are from the most recent order containing the series.
/// </summary>
public record RegularPurchase(int MonthsBought, int MonthsConsidered, int? LastIssueNumber, DateOnly? LastOrderDate);

/// <summary>Every current-solicitation item matched to one pull-list entry.</summary>
public record SolicitationMatch(int PullListEntryId, string PullListTitle, PullListStatus Status, IReadOnlyList<SolicitationItem> Items);

/// <summary>
/// FavoriteWriterMatches and Untracked are independent lenses over the same items, not
/// mutually exclusive categories - an item can appear in both (a title with a favorite writer
/// that also isn't on the pull list) or in neither. CartCheckedAt is when the cart behind each
/// item's IsInCheckedCart was checked - null if no cart has been, so "not in cart" means nothing.
/// </summary>
public record SolicitationCandidateList(
    DateTime? GeneratedAt,
    IReadOnlyList<SolicitationMatch> TrackedMatches,
    IReadOnlyList<SolicitationItem> FavoriteWriterMatches,
    IReadOnlyList<SolicitationItem> Untracked,
    DateTime? CartCheckedAt,
    int CartItemCount);

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

    /// <summary>Every crawled item, unfiltered - the full by-publisher browse (Solicitations tab) doesn't care about pull-list status at all.</summary>
    Task<IReadOnlyList<SolicitationItem>> GetAllItemsAsync(CancellationToken ct = default);
}
