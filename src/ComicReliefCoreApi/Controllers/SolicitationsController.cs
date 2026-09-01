using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Current DCBS solicitations, crawled directly from each publisher's own preorders
/// category page rather than assembled from a per-title search or a third-party database
/// (Comic Vine was tried and ruled out this session - see docs/BACKLOG.md - both for
/// coverage gaps and for lagging behind DCBS's actual solicitation month). Cross-
/// referenced against the pull list so a "candidate list" (step 2 of the user's original
/// workflow) and per-title archiving evidence both come from one crawl.
/// </summary>
[ApiController]
[Route("api/solicitations")]
public sealed class SolicitationsController : ControllerBase
{
    private readonly ISolicitationService _solicitations;
    private readonly ComicReliefDbContext _db;

    public SolicitationsController(ISolicitationService solicitations, ComicReliefDbContext db)
    {
        _solicitations = solicitations;
        _db = db;
    }

    /// <summary>Crawls every non-manga publisher category fresh - expect this to take a while (~20 real DCBS pages).</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<SolicitationRefreshResult>> Refresh(CancellationToken cancellationToken)
    {
        return Ok(await _solicitations.RefreshAsync(cancellationToken));
    }

    [HttpGet("status")]
    public async Task<ActionResult<SolicitationCacheStatus>> Status(CancellationToken cancellationToken)
    {
        return Ok(await _solicitations.GetStatusAsync(cancellationToken));
    }

    /// <summary>
    /// Cross-references the persisted crawl against the current pull list (archived
    /// titles excluded - Boot Hill isn't watching for new issues). Recomputed on every
    /// call, so pull-list changes show up without needing a new crawl.
    /// </summary>
    [HttpGet("candidates")]
    public async Task<ActionResult<SolicitationCandidateList>> Candidates(CancellationToken cancellationToken)
    {
        var trackedEntries = await _db.PullListEntries
            .Where(e => e.ArchivedAt == null)
            .ToListAsync(cancellationToken);

        return Ok(await _solicitations.BuildCandidateListAsync(trackedEntries, cancellationToken));
    }
}
