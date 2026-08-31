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
    private readonly IClzCollectionService _clzService;
    private readonly ComicReliefDbContext _db;

    public PullListController(IPullListService pullListService, IClzCollectionService clzService, ComicReliefDbContext db)
    {
        _pullListService = pullListService;
        _clzService = clzService;
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

    /// <summary>Archived titles are hidden by default - pass ?archived=true to see only those instead.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PullListEntryResponse>>> GetAll(
        [FromQuery] bool archived, CancellationToken cancellationToken)
    {
        var entries = await _db.PullListEntries
            .Where(e => archived ? e.ArchivedAt != null : e.ArchivedAt == null)
            .OrderBy(e => e.Title)
            .ToListAsync(cancellationToken);

        var issueDates = await _clzService.GetLastKnownIssueDatesAsync(
            entries.Select(e => e.NormalizedTitle), cancellationToken);

        return Ok(entries
            .Select(e => PullListEntryResponse.FromEntity(e, issueDates.GetValueOrDefault(e.NormalizedTitle)))
            .ToList());
    }

    /// <summary>Hides a title from the default pull-list view. Never touches DCBS itself.</summary>
    [HttpPost("{id:int}/archive")]
    public async Task<ActionResult<PullListEntryResponse>> Archive(int id, CancellationToken cancellationToken)
    {
        var entry = await _pullListService.SetArchivedAsync(id, archived: true, cancellationToken);
        return entry is null ? NotFound() : Ok(PullListEntryResponse.FromEntity(entry));
    }

    [HttpPost("{id:int}/unarchive")]
    public async Task<ActionResult<PullListEntryResponse>> Unarchive(int id, CancellationToken cancellationToken)
    {
        var entry = await _pullListService.SetArchivedAsync(id, archived: false, cancellationToken);
        return entry is null ? NotFound() : Ok(PullListEntryResponse.FromEntity(entry));
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
