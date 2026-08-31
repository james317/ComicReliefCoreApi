namespace ComicReliefCoreApi.Models;

/// <summary>
/// One tracked title. Mirrors what docs/pull-list.csv captured manually this session,
/// but as the live, queryable source of truth once the app owns this instead of a spreadsheet.
/// </summary>
public class PullListEntry
{
    public int Id { get; set; }

    /// <summary>Display name, as the user knows the title.</summary>
    public required string Title { get; set; }

    /// <summary>
    /// Lowercased, punctuation-stripped, leading-"the"-stripped form of <see cref="Title"/>,
    /// used to match against DCBS's own inconsistently-formatted series titles
    /// (it drops "The", apostrophes, and colons unpredictably - see backlog notes).
    /// </summary>
    public required string NormalizedTitle { get; set; }

    public PullListStatus Status { get; set; } = PullListStatus.Unresolved;

    public PullListFormat PreferredFormat { get; set; } = PullListFormat.Unknown;

    /// <summary>The series code returned by /ajax/PullListSearch, if a matching series was ever found.</summary>
    public string? DcbsSeriesCode { get; set; }

    /// <summary>
    /// The real pull-list entry id ("plid") once confirmed present on /account/pulllist.
    /// Only trust this when <see cref="LastVerifiedStickyAt"/> is set - a success response alone isn't proof.
    /// </summary>
    public string? DcbsPullListId { get; set; }

    /// <summary>Most recent purchased product code (e.g. "AUG264275") for this title, used to drive the order-form fallback.</summary>
    public string? LastKnownProductCode { get; set; }

    /// <summary>The DCBS order id that <see cref="LastKnownProductCode"/> came from.</summary>
    public string? LastKnownOrderId { get; set; }

    /// <summary>Which mechanism last resolved this title, for diagnostics.</summary>
    public PullListAddMethod? LastSuccessfulMethod { get; set; }

    /// <summary>Human-readable reason it's Unsticky (or currently unresolved), e.g. "no series entity found" vs "AddPullListItem 500 on a long series code".</summary>
    public string? FailureReason { get; set; }

    /// <summary>Freeform notes, matching the style already used in docs/pull-list.csv.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptedAt { get; set; }

    /// <summary>
    /// Last time Status == Sticky was confirmed by actually re-fetching /account/pulllist,
    /// not just trusting a JSON {"success":true} - both were shown this session to be capable of lying.
    /// </summary>
    public DateTime? LastVerifiedStickyAt { get; set; }

    public List<PullListAddAttempt> Attempts { get; set; } = new();
}
