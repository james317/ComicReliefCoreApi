using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

/// <summary>Raw persisted facts about the last-synced order - no matching/business logic, see ISolicitationService (.App) for that.</summary>
public interface IDcbsOrderSnapshotStore
{
    /// <summary>Wholesale replace - this only ever tracks one order at a time (the most recently synced), not a history.</summary>
    Task ReplaceAsync(string orderId, IReadOnlyList<DcbsOrderLine> lines, DateTime syncedAt, CancellationToken ct = default);

    /// <summary>Product codes from the stored order, normalized upper-invariant - DCBS's order page and listing pages disagree on casing (AUG264372 vs aug264372).</summary>
    Task<IReadOnlySet<string>> GetProductCodesAsync(CancellationToken ct = default);

    Task<(string? OrderId, DateTime? SyncedAt, int LineCount)> GetStatusAsync(CancellationToken ct = default);
}
