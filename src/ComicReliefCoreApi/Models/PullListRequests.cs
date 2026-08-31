namespace ComicReliefCoreApi.Models;

public record AddToPullListRequest(string Title);

public record PullListEntryResponse(
    int Id,
    string Title,
    PullListStatus Status,
    PullListFormat PreferredFormat,
    string? DcbsSeriesCode,
    string? DcbsPullListId,
    PullListAddMethod? LastSuccessfulMethod,
    string? FailureReason,
    DateTime? LastAttemptedAt,
    DateTime? LastVerifiedStickyAt)
{
    public static PullListEntryResponse FromEntity(PullListEntry entry) => new(
        entry.Id,
        entry.Title,
        entry.Status,
        entry.PreferredFormat,
        entry.DcbsSeriesCode,
        entry.DcbsPullListId,
        entry.LastSuccessfulMethod,
        entry.FailureReason,
        entry.LastAttemptedAt,
        entry.LastVerifiedStickyAt);
}
