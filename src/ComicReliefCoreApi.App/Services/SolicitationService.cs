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
    private readonly ILogger<SolicitationService> _logger;

    public SolicitationService(
        IDcbsClient dcbs, IDcbsSolicitationStore store, IDcbsOrderSnapshotStore orderStore,
        IWriterPreferenceService writerPreferences, ILogger<SolicitationService> logger)
    {
        _dcbs = dcbs;
        _store = store;
        _orderStore = orderStore;
        _writerPreferences = writerPreferences;
        _logger = logger;
    }

    private static readonly IReadOnlyList<string> NoWriters = Array.Empty<string>();

    private async Task<List<SolicitationItem>> LoadItemsAsync(CancellationToken ct)
    {
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
                    avoidMatches ?? NoWriters);
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
        var items = await LoadItemsAsync(ct);

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
        return new SolicitationCandidateList(lastRefreshedAt, matches, favoriteWriterMatches, untracked);
    }

    public async Task<IReadOnlyList<SolicitationItem>> GetAllItemsAsync(CancellationToken ct = default)
    {
        return await LoadItemsAsync(ct);
    }
}
