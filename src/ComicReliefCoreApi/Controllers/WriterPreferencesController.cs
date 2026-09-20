using ComicReliefCoreApi.App.Services;
using ComicReliefCoreApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace ComicReliefCoreApi.Controllers;

/// <summary>
/// Manages the favorite/avoid writer lists used to badge solicitation cards and populate the
/// Candidates page's "Favorite Writers" section - see IWriterPreferenceService and
/// CreatorCreditParser for the matching itself.
/// </summary>
[ApiController]
[Route("api/writerpreferences")]
public sealed class WriterPreferencesController : ControllerBase
{
    private readonly IWriterPreferenceService _writerPreferences;

    public WriterPreferencesController(IWriterPreferenceService writerPreferences)
    {
        _writerPreferences = writerPreferences;
    }

    /// <summary>Adds a writer, or flips an existing one's type if the name (normalized) is already tracked - see IWriterPreferenceService.UpsertAsync.</summary>
    [HttpPost]
    public async Task<ActionResult<WriterPreferenceResponse>> Upsert(
        [FromBody] UpsertWriterPreferenceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var entry = await _writerPreferences.UpsertAsync(request.Name, request.Type, cancellationToken);
        return Ok(WriterPreferenceResponse.FromEntity(entry));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WriterPreferenceResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var entries = await _writerPreferences.GetAllAsync(cancellationToken);
        return Ok(entries.Select(WriterPreferenceResponse.FromEntity).ToList());
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        return await _writerPreferences.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
