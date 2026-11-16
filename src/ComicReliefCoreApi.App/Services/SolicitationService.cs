using System.Collections.Concurrent;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;
using ComicReliefCoreApi.Api.Services;
using ComicReliefCoreApi.Api.Services.Dcbs;
using Microsoft.Extensions.Logging;

namespace ComicReliefCoreApi.App.Services;

public class SolicitationService : ISolicitationService
{
    // #1 or an explicit one-shot, per docs/BACKLOG.md's original spec for this feature -
    // deliberately narrower than PullListService's own OneShotOrSpecialTitle regex, which
    // also matches "Special" for a different purpose (skipping auto-tracking of a likely
    // one-off under an existing series' name). A "Special" for an already-ongoing series
    // isn't a new #1 debut the way a genuine one-shot for a brand-new property is, so it's
    // deliberately excluded here rather than reusing that regex as-is.
    private static readonly System.Text.RegularExpressions.Regex OneShotTitle =
        new(@"\bone[\s-]?shot\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsNewFirstIssueOrOneShot(DcbsListingItem item)
    {
        if (item.IsFacsimileOrReprint)
        {
            return false;
        }

        var issueNumber = IssueNumberParser.TryParseWholeIssueNumber(item.Title);
        if (issueNumber is not null)
        {
            return issueNumber == 1;
        }

        return OneShotTitle.IsMatch(item.Title);
    }

    // Polite to DCBS (this hits ~20 of its real pages per refresh) while still being much
    // faster than sequential - a handful of categories in flight at once is plenty given
    // each response is already several hundred KB.
    private const int MaxConcurrentCrawls = 4;

    private readonly IDcbsClient _dcbs;
    private readonly IDcbsSolicitationStore _store;
    private readonly IDcbsOrderSnapshotStore _orderStore;
    private readonly IWriterPreferenceService _writerPreferences;
    private readonly ICheckedCartStore _checkedCart;
    private readonly ILogger<SolicitationService> _logger;

    public SolicitationService(
        IDcbsClient dcbs, IDcbsSolicitationStore store, IDcbsOrderSnapshotStore orderStore,
        IWriterPreferenceService writerPreferences, ICheckedCartStore checkedCart, ILogger<SolicitationService> logger)
    {
        _dcbs = dcbs;
        _store = store;
        _orderStore = orderStore;
        _writerPreferences = writerPreferences;
        _checkedCart = checkedCart;
        _logger = logger;
    }

    private static readonly IReadOnlyList<string> NoWriters = Array.Empty<string>();

    private async Task<List<SolicitationItem>> LoadItemsAsync(CancellationToken ct)
    {
        var cart = await _checkedCart.GetAsync(ct);
        return await LoadItemsAsync(cart, ct);
    }

    private static bool IsInCart(DcbsListingItem item, HashSet<string> cartCodes, IReadOnlyList<string> uncodedCartTitles) =>
        cartCodes.Contains(item.ProductCode.ToUpperInvariant())
        || uncodedCartTitles.Any(t => CartItemIdentity.IsSameSeriesAndIssue(t, item.Title));

    private const int RegularMonthsWindow = 6;
    private const int RegularMinMonths = 2;

    // Per series (CartItemIdentity.SeriesKey), how many of the last RegularMonthsWindow months
    // with any order included it - only series meeting RegularMinMonths are returned. Months,
    // not orders: a bi-monthly book still counts, and small add-on orders between monthly ones
    // don't push real months out of the window. Cancelled lines and facsimile editions never
    // count (a "Batman #423 Facsimile" isn't buying Batman).
    private static Dictionary<string, RegularPurchase> BuildRegularPurchases(IReadOnlyList<DcbsOrderSnapshotLine> lines)
    {
        var bought = lines
            .Where(l => l.Status != DcbsShipmentStatus.Cancelled
                && l.OrderDate is not null
                && IssueNumberParser.TryParseWholeIssueNumber(l.Title) is not null
                && !l.Title.Contains("facsimile", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var recentMonths = bought
            .Select(l => (l.OrderDate!.Value.Year, l.OrderDate.Value.Month))
            .Distinct()
            .OrderByDescending(m => m)
            .Take(RegularMonthsWindow)
            .ToHashSet();

        var regulars = new Dictionary<string, RegularPurchase>();
        foreach (var series in bought.GroupBy(l => CartItemIdentity.SeriesKey(l.Title)))
        {
            var monthsBought = series
                .Select(l => (l.OrderDate!.Value.Year, l.OrderDate.Value.Month))
                .Where(recentMonths.Contains)
                .Distinct()
                .Count();
            if (monthsBought < RegularMinMonths)
            {
                continue;
            }
            var latest = series
                .OrderByDescending(l => long.TryParse(l.OrderId, out var id) ? id : 0)
                .ThenByDescending(l => IssueNumberParser.TryParseWholeIssueNumber(l.Title))
                .First();
            regulars[series.Key] = new RegularPurchase(
                monthsBought, recentMonths.Count, IssueNumberParser.TryParseWholeIssueNumber(latest.Title), latest.OrderDate);
        }
        return regulars;
    }

    private async Task<List<SolicitationItem>> LoadItemsAsync(IReadOnlyList<CheckedCartLine> cart, CancellationToken ct)
    {
        var regulars = BuildRegularPurchases(await _orderStore.GetAllFullLinesAsync(ct));
        var cartCodes = cart.Where(c => c.ProductCode is not null).Select(c => c.ProductCode!.ToUpperInvariant()).ToHashSet();
        var uncodedCartTitles = cart.Where(c => c.ProductCode is null).Select(c => c.Title).ToList();
        var rows = await _store.GetAllAsync(ct);
        var orderedCodes = await _orderStore.GetProductCodesAsync(ct);
        var writerPrefs = await _writerPreferences.GetAllAsync(ct);
        var favoritePrefs = writerPrefs.Where(w => w.Type == WriterPreferenceType.Favorite).ToList();
        var avoidPrefs = writerPrefs.Where(w => w.Type == WriterPreferenceType.Avoid).ToList();

        return rows
            .Select(r =>
            {
                List<string>? favoriteMatches = null;
                List<string>? avoidMatches = null;
                if (favoritePrefs.Count > 0 || avoidPrefs.Count > 0)
                {
                    foreach (var writer in CreatorCreditParser.ExtractWriterNames(r.Item.CreatorsAndDescription))
                    {
                        // Word-boundary prefix match (not exact-normalized-string equality) -
                        // tolerates a generational suffix (Jr./Sr./II/III/IV) either side might
                        // omit, e.g. a tracked "James Tynion" matching DCBS's own credit of
                        // "James Tynion IV" - see TitleNormalizer.IsLikelyNameMatch.
                        if (favoritePrefs.Any(p => TitleNormalizer.IsLikelyNameMatch(p.Name, writer)))
                        {
                            (favoriteMatches ??= new List<string>()).Add(writer);
                        }
                        else if (avoidPrefs.Any(p => TitleNormalizer.IsLikelyNameMatch(p.Name, writer)))
                        {
                            (avoidMatches ??= new List<string>()).Add(writer);
                        }
                    }
                }

                return new SolicitationItem(
                    r.Publisher,
                    r.Item,
                    orderedCodes.Contains(r.Item.ProductCode.ToUpperInvariant()),
                    IsNewFirstIssueOrOneShot(r.Item),
                    r.FirstSeenAt,
                    r.FirstSeenAt == r.RefreshedAt,
                    favoriteMatches ?? NoWriters,
                    avoidMatches ?? NoWriters,
                    IsInCart(r.Item, cartCodes, uncodedCartTitles),
                    !r.Item.IsFacsimileOrReprint && IssueNumberParser.TryParseWholeIssueNumber(r.Item.Title) is not null
                        ? regulars.GetValueOrDefault(CartItemIdentity.SeriesKey(r.Item.Title))
                        : null);
            })
            .ToList();
    }

    public async Task<SolicitationRefreshResult> RefreshAsync(CancellationToken ct = default)
    {
        using var throttle = new SemaphoreSlim(MaxConcurrentCrawls);
        var errors = new ConcurrentDictionary<string, string>();
        var crawled = new ConcurrentDictionary<string, IReadOnlyList<DcbsListingItem>>();
        var refreshedAt = DateTime.UtcNow;

        // Fetching is safe to run concurrently (no shared mutable state - each task's
        // result lands in its own ConcurrentDictionary slot). Persisting is not: the
        // DbContext behind _store is Scoped, one instance per request, and EF Core's
        // DbContext is not thread-safe for concurrent operations - writing here too
        // produced real "second operation started on this context" and duplicate-tracked-
        // entity errors live. So crawling stays parallel; writing happens afterward, one
        // publisher at a time, below.
        var tasks = DcbsPublisherCategories.All.Select(async category =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                crawled[category.DisplayName] = await _dcbs.GetPublisherListingAsync(category.Slug, category.CategoryId, ct);
            }
            catch (Exception ex)
            {
                // Deliberately don't touch this publisher's stored rows on failure - its
                // last successful crawl stays queryable instead of disappearing for one
                // bad request.
                _logger.LogWarning(ex, "Failed to crawl {Publisher} solicitations - leaving its last known data in place", category.DisplayName);
                errors[category.DisplayName] = ex.Message;
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        foreach (var (publisher, items) in crawled)
        {
            await _store.ReplacePublisherAsync(publisher, items, refreshedAt, ct);
        }

        var status = await GetStatusAsync(ct);
        return new SolicitationRefreshResult(status, errors.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    public async Task<SolicitationCacheStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (lastRefreshedAt, counts) = await _store.GetStatusAsync(ct);
        return new SolicitationCacheStatus(lastRefreshedAt, counts.Values.Sum(), counts);
    }

    public async Task<SolicitationCandidateList> BuildCandidateListAsync(
        IReadOnlyCollection<PullListEntry> trackedEntries, CancellationToken ct = default)
    {
        var cart = await _checkedCart.GetAsync(ct);
        var items = await LoadItemsAsync(cart, ct);

        var matched = new HashSet<SolicitationItem>();
        var matches = new List<SolicitationMatch>();

        foreach (var entry in trackedEntries)
        {
            // Facsimile/reprint editions of an old issue are excluded here even when they
            // match the series name - a plain pull-list entry like "Batman" means "the
            // current ongoing series," not every historical reprint DCBS happens to
            // resolicit the same month (real case: Batman #14, #227 Facsimile Edition, and
            // #423 Facsimile Edition all solicited together - only #14 belongs here). They
            // still show up in the full by-publisher browse (Solicitations tab), just not
            // as a pull-list match.
            var matchingItems = items
                .Where(i => !i.Item.IsFacsimileOrReprint && TitleNormalizer.IsLikelySeriesMatch(i.Item.Title, entry.Title))
                .ToList();

            if (matchingItems.Count == 0)
            {
                continue;
            }

            matches.Add(new SolicitationMatch(entry.Id, entry.Title, entry.Status, matchingItems));
            foreach (var item in matchingItems)
            {
                matched.Add(item);
            }
        }

        var untracked = items.Where(i => !matched.Contains(i)).ToList();
        var favoriteWriterMatches = items.Where(i => i.FavoriteWriters.Count > 0).ToList();
        var (lastRefreshedAt, _) = await _store.GetStatusAsync(ct);
        return new SolicitationCandidateList(
            lastRefreshedAt, matches, favoriteWriterMatches, untracked,
            cart.Count > 0 ? cart.Max(c => c.CheckedAt) : null, cart.Count);
    }

    public async Task<IReadOnlyList<SolicitationItem>> GetAllItemsAsync(CancellationToken ct = default)
    {
        return await LoadItemsAsync(ct);
    }
}
