using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

public sealed record SetIssueReleaseDateRequest(string Series, int IssueNumber, DateOnly? ReleaseDate);

/// <summary>
/// Lets the user's CLZ (Comic Book Collector) collection export be uploaded and re-uploaded
/// at will - each upload fully replaces the stored snapshot, since a CLZ export is always a
/// complete collection dump, not an incremental one. See wwwroot/pull-list.html for the
/// upload UI this backs, and IClzCollectionService's docs for why this data is labeled
/// "last purchased" rather than "last shipped" throughout the app.
/// </summary>
[ApiController]
[Route("api/clz")]
public sealed class ClzController : ControllerBase
{
    private readonly IClzCollectionService _clzService;

    public ClzController(IClzCollectionService clzService)
    {
        _clzService = clzService;
    }

    [HttpGet("status")]
    public async Task<ActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _clzService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }

    /// <summary>Full collection export only - wholesale replaces the stored snapshot. See wwwroot/pull-list.html for the warning shown next to this upload; a shipment-scoped export belongs on ImportShipmentIssues instead.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("File is required.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            await _clzService.ImportAsync(stream, cancellationToken);
            var status = await _clzService.GetStatusAsync(cancellationToken);
            return Ok(status);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>A shipment-scoped export (just the issues in one box) - merges into the per-issue release-date table only, never touches the full collection snapshot Import maintains.</summary>
    [HttpPost("import-shipment-issues")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult> ImportShipmentIssues(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("File is required.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var count = await _clzService.ImportShipmentIssuesAsync(stream, cancellationToken);
            return Ok(new { issuesUpserted = count });
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Manually sets one issue's release date - for the handful of items CLZ never has a
    /// clean date for on its own (no catalog record, or an unhelpful one like an original-
    /// printing date on a reprint). See IClzCollectionService for what "series" should read
    /// as here - a prefix of the DCBS shipment line's own title, not necessarily CLZ's field.
    /// </summary>
    [HttpPost("issue-release")]
    public async Task<ActionResult> SetIssueReleaseDate(
        [FromBody] SetIssueReleaseDateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Series))
        {
            return BadRequest("Series is required.");
        }

        await _clzService.SetIssueReleaseDateAsync(request.Series, request.IssueNumber, request.ReleaseDate, cancellationToken);
        return Ok();
    }
}
