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
}
