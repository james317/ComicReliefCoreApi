using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// One line item from a synced DCBS order, persisted across every order this account has
/// ever placed (see IDcbsOrderSnapshotStore) - not just the latest one, so a rescan can tell
/// a genuinely new solicitation apart from one already covered by an older order. Status is
/// the order page's own Processing/Filled/Shipped/Cancelled icon at sync time, null for the
/// few rows that never carry one (free items like Comic Shop News/monthly catalogs) - it's a
/// point-in-time snapshot, not re-checked between syncs.
/// </summary>
public class DcbsOrderSnapshotLine
{
    public int Id { get; set; }

    public required string OrderId { get; set; }

    public required string ProductCode { get; set; }

    public required string Title { get; set; }

    public DcbsShipmentStatus? Status { get; set; }

    public DateTime SyncedAt { get; set; }

    /// <summary>Null for the rare free-item rows (Comic Shop News, monthly catalogs) - same rows Status is already null for.</summary>
    public int? Quantity { get; set; }

    public decimal? UnitPrice { get; set; }

    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// This order's "Order Date" (see IDcbsClient.GetOrderDatesAsync) - denormalized onto
    /// every line of the order, same pattern as SyncedAt above, rather than a separate
    /// per-order header table. Null only if the order no longer appears on /account/orders
    /// at sync time (shouldn't happen for a real order, but not assumed).
    /// </summary>
    public DateOnly? OrderDate { get; set; }
}
