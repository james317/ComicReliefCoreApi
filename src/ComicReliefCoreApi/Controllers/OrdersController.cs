using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Syncs the user's DCBS order history so candidates can flag "matches your pull list,
/// not in anything you've ordered" - checked against every synced order, not just the
/// most recent one. See IOrderSnapshotService for why that distinction is real: a
/// different cover of an already-ordered issue can get resolicited later, and comparing
/// against only the latest order misses that it's already covered.
/// </summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderSnapshotService _orders;

    public OrdersController(IOrderSnapshotService orders)
    {
        _orders = orders;
    }

    /// <summary>Fetches up to maxOrders most recent orders from DCBS and upserts each into the stored history (default 24 - comfortably covers this account's entire ~20-order lifetime).</summary>
    [HttpPost("sync-recent")]
    public async Task<ActionResult<OrderSyncResult>> SyncRecent([FromQuery] int? maxOrders, CancellationToken cancellationToken)
    {
        return Ok(await _orders.SyncRecentAsync(maxOrders ?? 24, cancellationToken));
    }

    [HttpGet("status")]
    public async Task<ActionResult<OrderSnapshotStatus>> Status(CancellationToken cancellationToken)
    {
        return Ok(await _orders.GetStatusAsync(cancellationToken));
    }

    /// <summary>The most recently placed order's id/date, for the Solicitations page's "new since I built my order" view. Null if no orders exist.</summary>
    [HttpGet("most-recent-date")]
    public async Task<ActionResult> MostRecentDate(CancellationToken cancellationToken)
    {
        return Ok(await _orders.GetMostRecentOrderDateAsync(cancellationToken));
    }

    /// <summary>Full-detail search across every synced order's line items - see IOrderSnapshotService.SearchAsync. Never touches DCBS itself.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<OrderedItemSearchResult>>> Search(
        [FromQuery] string term, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return BadRequest("term is required.");
        }

        return Ok(await _orders.SearchAsync(term, cancellationToken));
    }
}
