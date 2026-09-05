using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Syncs the user's single most recent DCBS order so candidates can flag "matches your pull
/// list, not in your latest order" - see IOrderSnapshotService for why this only ever
/// tracks one order at a time.
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

    /// <summary>Fetches the most recent order from DCBS and replaces the stored snapshot with it.</summary>
    [HttpPost("sync-latest")]
    public async Task<ActionResult<OrderSnapshotStatus>> SyncLatest(CancellationToken cancellationToken)
    {
        return Ok(await _orders.SyncLatestAsync(cancellationToken));
    }

    [HttpGet("status")]
    public async Task<ActionResult<OrderSnapshotStatus>> Status(CancellationToken cancellationToken)
    {
        return Ok(await _orders.GetStatusAsync(cancellationToken));
    }
}
