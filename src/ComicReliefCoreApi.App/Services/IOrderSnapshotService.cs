namespace ComicReliefCoreApi.App.Services;

public record OrderSnapshotStatus(int OrderCount, int TotalLineCount, DateTime? LastSyncedAt);

/// <summary>Errors are only ever about the sync call that just ran - never persisted, since a failed order just leaves its last-known lines in place.</summary>
public record OrderSyncResult(OrderSnapshotStatus Status, IReadOnlyDictionary<string, string> OrderErrors);

/// <summary>
/// Persists the user's DCBS order history so a candidates rescan can flag "this matches
/// your pull list and isn't in anything you've ordered" - checked against every synced
/// order, not just the latest. Real gap this closes: a variant cover of an issue already
/// ordered can get solicited again later in a different cycle; comparing against only the
/// most recent order misses that it's already covered, and the user re-orders a duplicate.
/// </summary>
public interface IOrderSnapshotService
{
    /// <summary>Fetches up to maxOrders most recent orders from /account/orders and upserts each into the stored history (existing orders are refreshed, not duplicated).</summary>
    Task<OrderSyncResult> SyncRecentAsync(int maxOrders = 24, CancellationToken ct = default);

    Task<OrderSnapshotStatus> GetStatusAsync(CancellationToken ct = default);
}
