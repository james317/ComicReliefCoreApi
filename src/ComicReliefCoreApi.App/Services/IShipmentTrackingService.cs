using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.App.Services;

public interface IShipmentTrackingService
{
    /// <summary>Recent real boxes mailed to the user, newest first - pick one of these ids to pass to GetReadingOrderAsync.</summary>
    Task<IReadOnlyList<DcbsShipmentSummary>> GetRecentShipmentsAsync(int max = 12, CancellationToken ct = default);

    /// <summary>
    /// A shipment's items grouped by when each issue actually came out (from the uploaded
    /// CLZ export), for reading in ship-date order - the reading step of the user's own
    /// shipment-processing routine. Interest ranking within a date group is deliberately not
    /// modeled here: it's a personal, per-shipment judgment call the user makes themselves,
    /// not something this cross-reference can derive.
    /// </summary>
    Task<ShipmentReadingOrder> GetReadingOrderAsync(string shipmentId, CancellationToken ct = default);
}

/// <summary>ReleaseDate is null when no CLZ row matched this exact issue (not on file, or a title/issue-number mismatch) - shown as its own "unknown" group rather than guessed into one.</summary>
public sealed record ShipmentReadingItem(string ProductCode, string Title, DateOnly? ReleaseDate);

public sealed record ShipmentReadingGroup(DateOnly? ReleaseDate, IReadOnlyList<ShipmentReadingItem> Items);

public sealed record ShipmentReadingOrder(string ShipmentId, IReadOnlyList<ShipmentReadingGroup> Groups);
