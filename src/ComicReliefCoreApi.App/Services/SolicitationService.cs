using System.Collections.Concurrent;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;
using ComicReliefCoreApi.Api.Services;
using ComicReliefCoreApi.Api.Services.Dcbs;
using Microsoft.Extensions.Logging;

namespace ComicReliefCoreApi.App.Services;

public class SolicitationService : ISolicitationService
{
    // Polite to DCBS (this hits ~20 of its real pages per refresh) while still being much
    // faster than sequential - a handful of categories in flight at once is plenty given
    // each response is already several hundred KB.
    private const int MaxConcurrentCrawls = 4;

    private readonly IDcbsClient _dcbs;
    private readonly IDcbsSolicitationStore _store;
    private readonly ILogger<SolicitationService> _logger;

    public SolicitationService(IDcbsClient dcbs, IDcbsSolicitationStore store, ILogger<SolicitationService> logger)
    {
        _dcbs = dcbs;
        _store = store;
        _logger = logger;
    }

    public async Task<SolicitationRefreshResult> RefreshAsync(CancellationToken ct = default)
    {
        using var throttle = new SemaphoreSlim(MaxConcurrentCrawls);
        var errors = new ConcurrentDictionary<string, string>();
        var refreshedAt = DateTime.UtcNow;

        var tasks = DcbsPublisherCategories.All.Select(async category =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var items = await _dcbs.GetPublisherListingAsync(category.Slug, category.CategoryId, ct);
                await _store.ReplacePublisherAsync(category.DisplayName, items, refreshedAt, ct);
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
        var rows = await _store.GetAllAsync(ct);
        var items = rows.Select(r => new SolicitationItem(r.Publisher, r.Item)).ToList();

        var matched = new HashSet<SolicitationItem>();
        var matches = new List<SolicitationMatch>();

        foreach (var entry in trackedEntries)
        {
            var matchingItems = items
                .Where(i => TitleNormalizer.IsLikelySeriesMatch(i.Item.Title, entry.Title))
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
        var (lastRefreshedAt, _) = await _store.GetStatusAsync(ct);
        return new SolicitationCandidateList(lastRefreshedAt, matches, untracked);
    }
}
