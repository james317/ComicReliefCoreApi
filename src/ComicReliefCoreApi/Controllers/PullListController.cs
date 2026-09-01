using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models.Dcbs;
using ComicReliefCoreApi.Api.Services.Dcbs;
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
    private readonly IDcbsClient _dcbs;
    private readonly ComicReliefDbContext _db;

    public PullListController(
        IPullListService pullListService, IClzCollectionService clzService, IDcbsClient dcbs, ComicReliefDbContext db)
    {
        _pullListService = pullListService;
        _clzService = clzService;
        _dcbs = dcbs;
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

    /// <summary>
    /// Diagnostic-only passthrough to DCBS's own series search - raw facts, no matching
    /// or business decisions. Built to answer a specific question: does DCBS's search
    /// response include a usable "last shipped issue" fact anywhere (the CurrentIssueText
    /// field), as an alternative to the CLZ purchase-history proxy. Read-only against
    /// DCBS - never mutates anything.
    /// </summary>
    [HttpGet("dcbs-search")]
    public async Task<ActionResult<IReadOnlyList<DcbsSeriesSearchResult>>> DcbsSearch(
        [FromQuery] string term, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return BadRequest("term is required.");
        }

        var results = await _dcbs.SearchSeriesAsync(term, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// Diagnostic-only raw GET against any DCBS relative path, with an optional
    /// ProductsPerPage cookie override - built specifically to test whether that cookie
    /// actually accepts values beyond the 100 documented from DCBS's own UI dropdown, or
    /// whether it's capped server-side. Read-only. Returns a summary, not the full body,
    /// to keep responses small while exploring.
    /// </summary>
    [HttpGet("dcbs-raw")]
    public async Task<ActionResult> DcbsRaw(
        [FromQuery] string path, [FromQuery] int? productsPerPage, [FromQuery] int snippetOffset = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("path is required.");
        }

        var extraCookies = productsPerPage is { } n
            ? new Dictionary<string, string> { ["ProductsPerPage"] = n.ToString() }
            : null;

        var (statusCode, body) = await _dcbs.GetRawAsync(path, extraCookies, cancellationToken);
        var start = Math.Clamp(snippetOffset, 0, body.Length);
        var length = Math.Min(4000, body.Length - start);

        return Ok(new
        {
            statusCode,
            bodyLength = body.Length,
            dcbsPriceCount = CountOccurrences(body, "dcbsprice"),
            cartImgCount = CountOccurrences(body, "cartimg"),
            productLinkCount = CountOccurrences(body, "/product/"),
            categoryLinks = System.Text.RegularExpressions.Regex.Matches(body, "href=\"(/products/[^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Take(30)
                .ToList(),
            snippet = body.Substring(start, length),
        });
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
