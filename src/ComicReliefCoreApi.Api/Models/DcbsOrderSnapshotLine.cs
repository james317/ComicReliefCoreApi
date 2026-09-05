namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// One line item from the user's most recently synced DCBS order, persisted so a
/// solicitations rescan can flag "this matches your pull list and isn't in your latest
/// order" without re-fetching the order page every time. Replaced wholesale on each sync
/// (see IDcbsOrderSnapshotStore) - this only ever tracks one order at a time, the most
/// recent, not a running history.
/// </summary>
public class DcbsOrderSnapshotLine
{
    public int Id { get; set; }

    public required string OrderId { get; set; }

    public required string ProductCode { get; set; }

    public required string Title { get; set; }

    public DateTime SyncedAt { get; set; }
}
