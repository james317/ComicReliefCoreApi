using ComicReliefCoreApi.Api.Models.Dcbs;
using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Tracks real boxes mailed to the user (DCBS "shipments" - distinct from "orders", see
/// DcbsShipmentSummary) for the read-order step of the user's own shipment-processing
/// routine: cross-references each item against the uploaded CLZ export to group by when it
/// actually came out. No packing-list upload needed - the shipment detail page already has
/// everything (title, product code, qty) that a physical packlist would.
/// </summary>
[ApiController]
[Route("api/shipments")]
public sealed class ShipmentsController : ControllerBase
{
    private readonly IShipmentTrackingService _shipments;

    public ShipmentsController(IShipmentTrackingService shipments)
    {
        _shipments = shipments;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DcbsShipmentSummary>>> GetRecent(
        [FromQuery] int? max, CancellationToken cancellationToken)
    {
        return Ok(await _shipments.GetRecentShipmentsAsync(max ?? 12, cancellationToken));
    }

    [HttpGet("{shipmentId}/reading-order")]
    public async Task<ActionResult<ShipmentReadingOrder>> GetReadingOrder(
        string shipmentId, CancellationToken cancellationToken)
    {
        return Ok(await _shipments.GetReadingOrderAsync(shipmentId, cancellationToken));
    }
}
