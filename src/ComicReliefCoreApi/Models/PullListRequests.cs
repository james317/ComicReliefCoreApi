using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.App.Services;

namespace ComicReliefCoreApi.Models;

public record AddToPullListRequest(string Title);

public record ImportPullListRowRequest(string Title, PullListStatus Status, string? Notes)
{
    public PullListImportRow ToImportRow() => new(Title, Status, Notes);
}

public record ImportPullListRequest(List<ImportPullListRowRequest> Rows);

public record ImportPullListResponse(int Imported, int Skipped);

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
    DateTime? LastVerifiedStickyAt,
    DateTime? ArchivedAt)
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
        entry.LastVerifiedStickyAt,
        entry.ArchivedAt);
}
