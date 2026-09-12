using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Flags issue-number gaps in the pull list, cross-referenced against the full synced DCBS
/// order history (see IDcbsOrderSnapshotStore/IOrderSnapshotService) - e.g. owning #12 and
/// #14 of a series but never #13. Mirrors a manual process the user already runs on every
/// shipment: a "next expected issue" note, incremented on receipt, investigated whenever a
/// received issue skips ahead. This only surfaces candidates for that same investigation -
/// it never marks anything missing on its own, since a flagged gap can turn out to be a
/// mistake (wrong pull-list match, a title change) rather than a real missed issue.
/// </summary>
[ApiController]
[Route("api/missed-issues")]
public sealed class MissedIssuesController : ControllerBase
{
    private readonly IIssueContinuityService _continuity;

    public MissedIssuesController(IIssueContinuityService continuity)
    {
        _continuity = continuity;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MissedIssueFlag>>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _continuity.CheckMissedIssuesAsync(cancellationToken));
    }
}
