using ComicReliefCoreApi.Api.Services.Dcbs;

namespace ComicReliefCoreApi.App.Services;

public class OrderSnapshotService : IOrderSnapshotService
{
    private readonly IDcbsClient _dcbs;
    private readonly IDcbsOrderSnapshotStore _store;

    public OrderSnapshotService(IDcbsClient dcbs, IDcbsOrderSnapshotStore store)
    {
        _dcbs = dcbs;
        _store = store;
    }

    public async Task<OrderSnapshotStatus> SyncLatestAsync(CancellationToken ct = default)
    {
        var orderIds = await _dcbs.GetRecentOrderIdsAsync(1, ct);
        var orderId = orderIds.FirstOrDefault();
        if (orderId is null)
        {
            return await GetStatusAsync(ct);
        }

        var lines = await _dcbs.GetOrderLinesAsync(orderId, ct);
        await _store.ReplaceAsync(orderId, lines, DateTime.UtcNow, ct);
        return await GetStatusAsync(ct);
    }

    public async Task<OrderSnapshotStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (orderId, syncedAt, count) = await _store.GetStatusAsync(ct);
        return new OrderSnapshotStatus(orderId, syncedAt, count);
    }
}
