using System.Collections.Concurrent;
using ComicReliefCoreApi.Api.Models.Dcbs;
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
    private readonly ILogger<OrderSnapshotService> _logger;

    public OrderSnapshotService(IDcbsClient dcbs, IDcbsOrderSnapshotStore store, ILogger<OrderSnapshotService> logger)
    {
        _dcbs = dcbs;
        _store = store;
        _logger = logger;
    }

    public async Task<OrderSyncResult> SyncRecentAsync(int maxOrders = 24, CancellationToken ct = default)
    {
        var orderIds = await _dcbs.GetRecentOrderIdsAsync(maxOrders, ct);
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
            await _store.UpsertOrderAsync(orderId, lines, syncedAt, ct);
        }

        var status = await GetStatusAsync(ct);
        return new OrderSyncResult(status, errors.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    public async Task<OrderSnapshotStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (orderCount, totalLineCount, lastSyncedAt) = await _store.GetStatusAsync(ct);
        return new OrderSnapshotStatus(orderCount, totalLineCount, lastSyncedAt);
    }
}
