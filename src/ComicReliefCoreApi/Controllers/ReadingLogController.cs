using ComicReliefCoreApi.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

public sealed record RecordReadRequest(string Series, int IssueNumber);
public sealed record BackfillEntry(string Series, int IssueNumber);
public sealed record BackfillRequest(IReadOnlyList<BackfillEntry> Entries);

/// <summary>
/// A guard against reading out of release order or accidentally skipping an issue while
/// working through a backlog: search what you own, log an issue as read, and see what else
/// shipped the same week (see IReadingLogService for why "same release date" = "same week").
/// </summary>
[ApiController]
[Route("api/reading-log")]
public sealed class ReadingLogController : ControllerBase
{
    private readonly IReadingLogService _readingLog;

    public ReadingLogController(IReadingLogService readingLog)
    {
        _readingLog = readingLog;
    }

    [HttpGet("search-owned")]
    public async Task<ActionResult<IReadOnlyList<OwnedIssueView>>> SearchOwned(
        [FromQuery] string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("query is required.");
        }
        return Ok(await _readingLog.SearchOwnedIssuesAsync(query, cancellationToken));
    }

    [HttpPost("read")]
    public async Task<ActionResult> RecordRead([FromBody] RecordReadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Series))
        {
            return BadRequest("Series is required.");
        }
        await _readingLog.RecordReadAsync(request.Series, request.IssueNumber, cancellationToken);
        return Ok();
    }

    /// <summary>One-time import of already-read issues with no known real dates, in the given order - see IReadingLogService.BackfillAsync.</summary>
    [HttpPost("backfill")]
    public async Task<ActionResult> Backfill([FromBody] BackfillRequest request, CancellationToken cancellationToken)
    {
        if (request.Entries.Count == 0)
        {
            return BadRequest("Entries is required.");
        }
        var count = await _readingLog.BackfillAsync(
            request.Entries.Select(e => (e.Series, e.IssueNumber)).ToList(), cancellationToken);
        return Ok(new { imported = count });
    }

    [HttpGet("same-week")]
    public async Task<ActionResult<ReadingLogWeekView>> GetSameWeek(
        [FromQuery] string series, [FromQuery] int issueNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(series))
        {
            return BadRequest("series is required.");
        }
        var view = await _readingLog.GetSameWeekAsync(series, issueNumber, cancellationToken);
        return view is null ? NotFound("No release date on file for that issue.") : Ok(view);
    }

    [HttpGet("recent")]
    public async Task<ActionResult> GetRecent([FromQuery] int? max, CancellationToken cancellationToken)
    {
        return Ok(await _readingLog.GetRecentlyReadAsync(max ?? 20, cancellationToken));
    }
}
