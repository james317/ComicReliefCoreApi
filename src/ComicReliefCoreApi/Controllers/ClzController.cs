using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Lets the user's CLZ (Comic Book Collector) collection export be uploaded and re-uploaded
/// at will - each upload fully replaces the stored snapshot, since a CLZ export is always a
/// complete collection dump, not an incremental one. See wwwroot/pull-list.html for the
/// upload UI this backs, and IClzCollectionService's docs for why this data is labeled
/// "last owned issue" rather than "last shipped issue" throughout the app.
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
}
