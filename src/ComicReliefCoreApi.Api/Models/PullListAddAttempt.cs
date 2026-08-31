namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// Audit record of one attempt to resolve a <see cref="PullListEntry"/> against DCBS.
/// Kept permanently rather than overwritten, since this session's own debugging showed
/// how much time re-deriving "why did this fail last time" can cost without a record.
/// </summary>
public class PullListAddAttempt
{
    public int Id { get; set; }

    public int PullListEntryId { get; set; }
    public PullListEntry? PullListEntry { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    public PullListAddMethod Method { get; set; }

    public bool Success { get; set; }

    /// <summary>Raw response body (JSON error message, or an HTTP 500 page snippet) for later diagnosis.</summary>
    public string? RawResponse { get; set; }

    public string? Notes { get; set; }
}
