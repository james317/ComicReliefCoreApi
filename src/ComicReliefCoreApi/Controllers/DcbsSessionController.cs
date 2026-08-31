using ComicReliefCoreApi.App.Services;
using ComicReliefCoreApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Lets a fresh DCBS session cookie be pasted in at runtime instead of requiring a Fly
/// secret update and redeploy every time it expires. See wwwroot/dcbs-session.html for
/// the paste-it-in UI this backs.
/// </summary>
[ApiController]
[Route("api/dcbs-session")]
public sealed class DcbsSessionController : ControllerBase
{
    private readonly IDcbsSessionManager _sessionManager;

    public DcbsSessionController(IDcbsSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [HttpGet("status")]
    public async Task<ActionResult<DcbsSessionStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _sessionManager.GetStatusAsync(cancellationToken);
        return Ok(DcbsSessionStatusResponse.FromStatus(status));
    }

    [HttpPost]
    public async Task<ActionResult<DcbsSessionStatusResponse>> Set(
        [FromBody] SetDcbsSessionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Cookie))
        {
            return BadRequest("Cookie is required.");
        }

        var status = await _sessionManager.SetAndValidateAsync(request.Cookie, cancellationToken);
        return Ok(DcbsSessionStatusResponse.FromStatus(status));
    }

    [HttpPost("revalidate")]
    public async Task<ActionResult<DcbsSessionStatusResponse>> Revalidate(CancellationToken cancellationToken)
    {
        var status = await _sessionManager.RevalidateAsync(cancellationToken);
        return Ok(DcbsSessionStatusResponse.FromStatus(status));
    }
}
