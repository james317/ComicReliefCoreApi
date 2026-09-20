using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.Models;

public record FlagFromListingRequest(
    string ListingTitle, string? Publisher, string? ProductCode, string? ProductUrl, string? Notes);

public record ReviewFlagResponse(
    int Id,
    string Title,
    string? Publisher,
    string? ProductCode,
    string? ProductUrl,
    string? Notes,
    DateTime FlaggedAt,
    DateTime? ResolvedAt)
{
    public static ReviewFlagResponse FromEntity(ReviewFlagEntry entry) => new(
        entry.Id,
        entry.Title,
        entry.Publisher,
        entry.ProductCode,
        entry.ProductUrl,
        entry.Notes,
        entry.FlaggedAt,
        entry.ResolvedAt);
}

public record ReviewFlagsListResponse(IReadOnlyList<ReviewFlagResponse> Flags, DateOnly? OrderEditCutoffDate);
