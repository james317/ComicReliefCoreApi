using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

/// <summary>
/// Raw persisted facts about every order synced so far - no matching/business logic, see
/// ISolicitationService (.App) for that. Tracks a real history, not just the latest order:
/// a solicitation-matching check against only the most recent order can miss that an
/// earlier order already covered a different cover/printing of the same issue.
/// </summary>
public interface IDcbsOrderSnapshotStore
{
    /// <summary>Replaces only this order's lines (delete-then-insert scoped to its OrderId) - safe to re-sync an already-known order without touching any other order's data. orderDate is denormalized onto every line (see DcbsOrderSnapshotLine.OrderDate) - null when the order's date couldn't be determined at sync time.</summary>
    Task UpsertOrderAsync(
        string orderId, IReadOnlyList<DcbsOrderLine> lines, DateOnly? orderDate, DateTime syncedAt, CancellationToken ct = default);

    /// <summary>Product codes across every synced order, normalized upper-invariant - DCBS's order page and listing pages disagree on casing (AUG264372 vs aug264372).</summary>
    Task<IReadOnlySet<string>> GetProductCodesAsync(CancellationToken ct = default);

    Task<(int OrderCount, int TotalLineCount, DateTime? LastSyncedAt)> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Every synced line across every order (OrderId, Title, and last-known Status) - the raw material for cross-title checks like issue-continuity gaps, which need the full history, not just product codes.</summary>
    Task<IReadOnlyList<(string OrderId, string Title, DcbsShipmentStatus? Status)>> GetAllLinesAsync(CancellationToken ct = default);

    /// <summary>Every synced line (full detail, every field) whose Title contains term - case-insensitive, across every order on file. No relevance ranking - a personal order history is small enough that "everything that matches, newest order first" is enough.</summary>
    Task<IReadOnlyList<DcbsOrderSnapshotLine>> SearchLinesAsync(string term, CancellationToken ct = default);
}
