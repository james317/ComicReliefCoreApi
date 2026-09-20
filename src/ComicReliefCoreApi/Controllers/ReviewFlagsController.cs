using ComicReliefCoreApi.App.Services;
using ComicReliefCoreApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

[ApiController]
[Route("api/reviewflags")]
public sealed class ReviewFlagsController : ControllerBase
{
    private readonly IReviewFlagService _reviewFlagService;

    public ReviewFlagsController(IReviewFlagService reviewFlagService)
    {
        _reviewFlagService = reviewFlagService;
    }

    /// <summary>
    /// Flags a solicitation card's exact listing title (issue number/cover/"(MR)" and all) -
    /// unlike pull-list adds, this is never stripped down to a bare series name, since a
    /// flag is about one specific solicited item ("not enough info to decide yet"), not a
    /// recurring series to track going forward.
    /// </summary>
    [HttpPost("flag-from-listing")]
    public async Task<ActionResult<ReviewFlagResponse>> FlagFromListing(
        [FromBody] FlagFromListingRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ListingTitle))
        {
            return BadRequest("ListingTitle is required.");
        }

        var entry = await _reviewFlagService.FlagAsync(
            request.ListingTitle, request.Publisher, request.ProductCode, request.ProductUrl, request.Notes,
            cancellationToken);
        return Ok(ReviewFlagResponse.FromEntity(entry));
    }

    /// <summary>Every open (unresolved) flag, plus the current DCBS order-edit cutoff date for the reminder banner's countdown.</summary>
    [HttpGet]
    public async Task<ActionResult<ReviewFlagsListResponse>> GetOpen(CancellationToken cancellationToken)
    {
        var flags = await _reviewFlagService.GetOpenAsync(cancellationToken);
        var cutoffDate = await _reviewFlagService.GetOrderEditCutoffDateAsync(cancellationToken);
        return Ok(new ReviewFlagsListResponse(
            flags.Select(ReviewFlagResponse.FromEntity).ToList(), cutoffDate));
    }

    [HttpPost("{id:int}/resolve")]
    public async Task<ActionResult<ReviewFlagResponse>> Resolve(int id, CancellationToken cancellationToken)
    {
        var entry = await _reviewFlagService.ResolveAsync(id, cancellationToken);
        return entry is null ? NotFound() : Ok(ReviewFlagResponse.FromEntity(entry));
    }
}
