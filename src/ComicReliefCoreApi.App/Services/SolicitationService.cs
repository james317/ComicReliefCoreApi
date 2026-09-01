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
    private readonly ILogger<SolicitationService> _logger;

    private readonly object _lock = new();
    private IReadOnlyList<SolicitationItem> _cachedItems = Array.Empty<SolicitationItem>();
    private DateTime? _lastRefreshedAt;
    private IReadOnlyDictionary<string, int> _publisherItemCounts = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, string> _publisherErrors = new Dictionary<string, string>();

    public SolicitationService(IDcbsClient dcbs, ILogger<SolicitationService> logger)
    {
        _dcbs = dcbs;
        _logger = logger;
    }

    public async Task<SolicitationCacheStatus> RefreshAsync(CancellationToken ct = default)
    {
        using var throttle = new SemaphoreSlim(MaxConcurrentCrawls);
        var allItems = new ConcurrentBag<SolicitationItem>();
        var counts = new ConcurrentDictionary<string, int>();
        var errors = new ConcurrentDictionary<string, string>();

        var tasks = DcbsPublisherCategories.All.Select(async category =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var items = await _dcbs.GetPublisherListingAsync(category.Slug, category.CategoryId, ct);
                counts[category.DisplayName] = items.Count;
                foreach (var item in items)
                {
                    allItems.Add(new SolicitationItem(category.DisplayName, item));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to crawl {Publisher} solicitations", category.DisplayName);
                errors[category.DisplayName] = ex.Message;
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        lock (_lock)
        {
            _cachedItems = allItems.ToList();
            _lastRefreshedAt = DateTime.UtcNow;
            _publisherItemCounts = counts.ToDictionary(kv => kv.Key, kv => kv.Value);
            _publisherErrors = errors.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        return GetStatus();
    }

    public SolicitationCacheStatus GetStatus()
    {
        lock (_lock)
        {
            return new SolicitationCacheStatus(_lastRefreshedAt, _cachedItems.Count, _publisherItemCounts, _publisherErrors);
        }
    }

    public SolicitationCandidateList BuildCandidateList(IReadOnlyCollection<PullListEntry> trackedEntries)
    {
        IReadOnlyList<SolicitationItem> items;
        DateTime? generatedAt;
        lock (_lock)
        {
            items = _cachedItems;
            generatedAt = _lastRefreshedAt;
        }

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
        return new SolicitationCandidateList(generatedAt, matches, untracked);
    }
}
