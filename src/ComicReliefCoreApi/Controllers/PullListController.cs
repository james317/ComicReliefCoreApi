using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.App.Services;
using ComicReliefCoreApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Controllers;

[ApiController]
[Route("api/pulllist")]
public sealed class PullListController : ControllerBase
{
    private readonly IPullListService _pullListService;
    private readonly ComicReliefDbContext _db;

    public PullListController(IPullListService pullListService, ComicReliefDbContext db)
    {
        _pullListService = pullListService;
        _db = db;
    }

    /// <summary>
    /// Given a title, tries DCBS's real sticky pull list first; if it can't be added
    /// there (no series record, or a known server bug), falls back to tracking it on
    /// our own unsticky list with a specific reason instead of a generic failure.
    /// </summary>
    [HttpPost("add")]
    public async Task<ActionResult<PullListEntryResponse>> Add(
        [FromBody] AddToPullListRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Title is required.");
        }

        var entry = await _pullListService.AddToPullListAsync(request.Title, cancellationToken);
        return Ok(PullListEntryResponse.FromEntity(entry));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PullListEntryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var entries = await _db.PullListEntries
            .OrderBy(e => e.Title)
            .ToListAsync(cancellationToken);
        return Ok(entries.Select(PullListEntryResponse.FromEntity).ToList());
    }

    /// <summary>
    /// One-time seed for titles whose Sticky/Unsticky status is already known from a live
    /// DCBS snapshot (docs/pull-list.csv) - skips DCBS entirely, so it's fast and doesn't
    /// touch the real account. Existing entries (by normalized title) are left untouched.
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportPullListResponse>> Import(
        [FromBody] ImportPullListRequest request, CancellationToken cancellationToken)
    {
        if (request.Rows.Count == 0)
        {
            return BadRequest("Rows is required.");
        }

        var imported = await _pullListService.ImportKnownEntriesAsync(
            request.Rows.Select(r => r.ToImportRow()), cancellationToken);
        return Ok(new ImportPullListResponse(imported, request.Rows.Count - imported));
    }
}
