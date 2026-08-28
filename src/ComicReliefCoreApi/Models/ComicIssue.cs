namespace ComicReliefCoreApi.Models;

/// <summary>A single comic issue shipping in the requested month, shaped for API consumers.</summary>
public sealed record ComicIssue(
    int Id,
    string Title,
    string? IssueNumber,
    DateOnly? StoreDate,
    string? CoverImageUrl,
    string? DetailUrl);

/// <summary>Result of a request for comics shipping within a given month.</summary>
public sealed record UpcomingComicsResponse(
    int Year,
    int Month,
    string MonthName,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    int Count,
    bool Truncated,
    IReadOnlyList<ComicIssue> Comics);
