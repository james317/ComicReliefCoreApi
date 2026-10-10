using System.Collections.Concurrent;
using ComicReliefCoreApi.Api.Models.Dcbs;
using ComicReliefCoreApi.Api.Services;
using ComicReliefCoreApi.Api.Services.Dcbs;
using Microsoft.Extensions.Logging;

namespace ComicReliefCoreApi.App.Services;

public class OrderSnapshotService : IOrderSnapshotService
{
    // Same reasoning as SolicitationService.MaxConcurrentCrawls - polite to DCBS, still
    // much faster than sequential for the ~20 order pages this account currently has.
    private const int MaxConcurrentOrderFetches = 4;

    private readonly IDcbsClient _dcbs;
    private readonly IDcbsOrderSnapshotStore _store;
    private readonly IDcbsSolicitationStore _solicitationStore;
    private readonly IPullListService _pullList;
    private readonly ILogger<OrderSnapshotService> _logger;

    public OrderSnapshotService(
        IDcbsClient dcbs, IDcbsOrderSnapshotStore store, IDcbsSolicitationStore solicitationStore,
        IPullListService pullList, ILogger<OrderSnapshotService> logger)
    {
        _dcbs = dcbs;
        _store = store;
        _solicitationStore = solicitationStore;
        _pullList = pullList;
        _logger = logger;
    }

    public async Task<OrderSyncResult> SyncRecentAsync(int maxOrders = 24, CancellationToken ct = default)
    {
        var orderIds = await _dcbs.GetRecentOrderIdsAsync(maxOrders, ct);
        var orderDates = await _dcbs.GetOrderDatesAsync(ct);
        var errors = new ConcurrentDictionary<string, string>();
        var fetched = new ConcurrentDictionary<string, IReadOnlyList<DcbsOrderLine>>();
        var syncedAt = DateTime.UtcNow;

        // Fetching concurrently is safe (no shared mutable state); persisting is not - see
        // SolicitationService.RefreshAsync for why writes to the Scoped DbContext have to
        // happen sequentially, one order at a time, after all fetches complete.
        using var throttle = new SemaphoreSlim(MaxConcurrentOrderFetches);
        var tasks = orderIds.Select(async orderId =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                fetched[orderId] = await _dcbs.GetOrderLinesAsync(orderId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch order {OrderId} - leaving its last known lines in place", orderId);
                errors[orderId] = ex.Message;
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);

        foreach (var (orderId, lines) in fetched)
        {
            DateOnly? orderDate = orderDates.TryGetValue(orderId, out var d) ? d : null;
            await _store.UpsertOrderAsync(orderId, lines, orderDate, syncedAt, ct);
        }

        var newFirstIssues = await _pullList.DetectAndTrackNewFirstIssuesAsync(ct);

        var status = await GetStatusAsync(ct);
        return new OrderSyncResult(status, errors.ToDictionary(kv => kv.Key, kv => kv.Value), newFirstIssues);
    }

    public async Task<OrderSnapshotStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (orderCount, totalLineCount, lastSyncedAt) = await _store.GetStatusAsync(ct);
        return new OrderSnapshotStatus(orderCount, totalLineCount, lastSyncedAt);
    }

    public Task<DcbsOrderDateInfo?> GetMostRecentOrderDateAsync(CancellationToken ct = default) =>
        _dcbs.GetMostRecentOrderDateAsync(ct);

    public async Task<IReadOnlyList<OrderedItemSearchResult>> SearchAsync(string term, CancellationToken ct = default)
    {
        var lines = await _store.SearchLinesAsync(term, ct);
        var solicited = await _solicitationStore.GetAllAsync(ct);
        // Last-write-wins on a duplicate product code (a relisted item can appear more than
        // once across categories) - fine here, any one match is equally good for a details
        // lookup, unlike SolicitationService's own matching which cares about every listing.
        var byProductCode = solicited.ToDictionary(
            s => s.Item.ProductCode.ToUpperInvariant(), s => s, StringComparer.Ordinal);

        return lines.Select(l =>
        {
            byProductCode.TryGetValue(l.ProductCode.ToUpperInvariant(), out var match);
            return new OrderedItemSearchResult(
                l.OrderId,
                l.OrderDate,
                l.ProductCode,
                l.Title,
                l.Quantity,
                l.UnitPrice,
                l.Status,
                match.Item?.ThumbnailUrl ?? l.ThumbnailUrl,
                match.Item?.ProductUrl,
                match.Publisher);
        }).ToList();
    }

    // Below this length a normalized fragment ("1", "Qty", a lone cover letter) is a substring
    // of nearly every title on file - matching it would flood every pasted line with bogus
    // duplicates instead of the real ones. Real series/issue titles normalize to well past
    // this; nothing legitimate gets excluded by it.
    private const int MinNormalizedLengthToMatch = 4;

    public async Task<IReadOnlyList<CartLineCheckResult>> CheckCartForDuplicatesAsync(
        IReadOnlyList<string> pastedLines, CancellationToken ct = default)
    {
        var allLines = await _store.GetAllFullLinesAsync(ct);
        var solicited = await _solicitationStore.GetAllAsync(ct);
        var byProductCode = solicited.ToDictionary(
            s => s.Item.ProductCode.ToUpperInvariant(), s => s, StringComparer.Ordinal);

        var results = new List<CartLineCheckResult>();
        foreach (var rawLine in pastedLines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var normalizedLine = TitleNormalizer.Normalize(line);

            // Bidirectional, same idiom as PullListService.FindKnownOrRecentPurchaseAsync -
            // pasted cart text usually carries extra noise (price, qty, cover letter) that a
            // one-directional Contains (the clean-search-term case SearchLinesAsync handles)
            // would reject as "no match" even when it's obviously the same item.
            var matches = allLines
                .Where(l =>
                {
                    var normalizedTitle = TitleNormalizer.Normalize(l.Title);
                    if (normalizedTitle.Length < MinNormalizedLengthToMatch || normalizedLine.Length < MinNormalizedLengthToMatch)
                    {
                        return false;
                    }
                    return normalizedTitle.Contains(normalizedLine, StringComparison.Ordinal)
                        || normalizedLine.Contains(normalizedTitle, StringComparison.Ordinal);
                })
                .Select(l =>
                {
                    byProductCode.TryGetValue(l.ProductCode.ToUpperInvariant(), out var match);
                    return new OrderedItemSearchResult(
                        l.OrderId, l.OrderDate, l.ProductCode, l.Title, l.Quantity, l.UnitPrice, l.Status,
                        match.Item?.ThumbnailUrl ?? l.ThumbnailUrl, match.Item?.ProductUrl, match.Publisher);
                })
                .OrderByDescending(m => m.OrderDate)
                .ToList();

            results.Add(new CartLineCheckResult(rawLine, matches));
        }

        return results;
    }
}
