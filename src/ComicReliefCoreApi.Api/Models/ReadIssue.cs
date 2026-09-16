namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// One "I just read this issue" log entry - a guard against reading out of release order or
/// accidentally skipping an issue while working through a backlog. ReadAt is null for
/// backfilled history where only the order was known, not real dates (see docs/BACKLOG.md) -
/// Id (insertion order) is the source of truth for ordering, not ReadAt, since backfilled
/// rows are always inserted in the order they were actually read, and every row read from
/// now on is inserted after all of them regardless of whether it also gets a real ReadAt.
/// </summary>
public class ReadIssue
{
    public int Id { get; set; }

    /// <summary>Display name, as recorded.</summary>
    public required string Series { get; set; }

    /// <summary>Normalized via TitleNormalizer - matched against ClzIssueRelease.NormalizedSeries to look up release dates.</summary>
    public required string NormalizedSeries { get; set; }

    public int IssueNumber { get; set; }

    /// <summary>Null when only the read order (not the date) is known - see class docs.</summary>
    public DateTime? ReadAt { get; set; }
}
