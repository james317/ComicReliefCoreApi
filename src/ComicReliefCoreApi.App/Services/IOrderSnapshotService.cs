namespace ComicReliefCoreApi.App.Services;

public record OrderSnapshotStatus(string? OrderId, DateTime? SyncedAt, int LineCount);

/// <summary>
/// Persists the user's single most recent DCBS order so a candidates rescan can flag "this
/// matches your pull list and isn't in your latest order" - the real gap this closes: the
/// Candidates page could only ever say "this is currently solicited," never "did you
/// actually order it." Deliberately tracks only one order (the latest), not a history -
/// re-syncing after placing a new order is the expected monthly workflow.
/// </summary>
public interface IOrderSnapshotService
{
    /// <summary>Fetches the single most recent order from /account/orders and replaces the stored snapshot with it.</summary>
    Task<OrderSnapshotStatus> SyncLatestAsync(CancellationToken ct = default);

    Task<OrderSnapshotStatus> GetStatusAsync(CancellationToken ct = default);
}
