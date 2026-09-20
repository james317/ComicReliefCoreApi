namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// A solicitation the user marked "not enough information yet" - too obscure/new for the
/// Scuttlebutt-style content/review lookup to find anything, but not a "no" either. Sits
/// separately from PullListEntry since flagging isn't a pull-list decision at all - it's a
/// "come back to this before the order cutoff" note that may or may not end up on the pull
/// list once more information surfaces.
/// </summary>
public class ReviewFlagEntry
{
    public int Id { get; set; }

    /// <summary>Full listing title as scraped (issue number, cover, "(MR)" and all) - not stripped down to a bare series name, since this is about one specific solicited item, not a recurring series.</summary>
    public required string Title { get; set; }

    public string? Publisher { get; set; }

    public string? ProductCode { get; set; }

    public string? ProductUrl { get; set; }

    /// <summary>Optional freeform reason, e.g. why it needs a second look.</summary>
    public string? Notes { get; set; }

    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set once the user has revisited and decided either way - hidden from the open-flags banner once set.</summary>
    public DateTime? ResolvedAt { get; set; }
}
