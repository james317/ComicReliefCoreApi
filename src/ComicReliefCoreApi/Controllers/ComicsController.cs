using ComicReliefCoreApi.Models;
using ComicReliefCoreApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

[ApiController]
[Route("api/comics")]
public sealed class ComicsController : ControllerBase
{
    private readonly IComicVineService _comicVineService;

    public ComicsController(IComicVineService comicVineService)
    {
        _comicVineService = comicVineService;
    }

    /// <summary>
    /// Comics shipping in a given month. Defaults to "month after next" (today's
    /// month + 2), which is the furthest-out month Diamond/local shops typically
    /// have solicited. Override with ?year=&amp;month= for any other month.
    /// </summary>
    [HttpGet("upcoming")]
    public async Task<ActionResult<UpcomingComicsResponse>> GetUpcoming(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken cancellationToken)
    {
        if (year is null != month is null)
        {
            return BadRequest("Pass both year and month, or neither.");
        }

        DateOnly targetMonth;
        if (year is not null && month is not null)
        {
            if (month is < 1 or > 12)
            {
                return BadRequest("month must be between 1 and 12.");
            }

            try
            {
                targetMonth = new DateOnly(year.Value, month.Value, 1);
            }
            catch (ArgumentOutOfRangeException)
            {
                return BadRequest("year/month is out of range.");
            }
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var twoMonthsOut = today.AddMonths(2);
            targetMonth = new DateOnly(twoMonthsOut.Year, twoMonthsOut.Month, 1);
        }

        var rangeStart = targetMonth;
        var rangeEnd = targetMonth.AddMonths(1).AddDays(-1);

        try
        {
            var result = await _comicVineService.GetIssuesShippingInAsync(rangeStart, rangeEnd, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
