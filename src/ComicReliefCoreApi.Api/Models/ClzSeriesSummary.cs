namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// One row per series from the user's CLZ (Comic Book Collector) collection export,
/// aggregated at import time to the most recent issue's release (ship) date and how many
/// issues of that series are owned. This is "when the user last purchased an issue of
/// this series" - not proof the publisher stopped shipping it. A gap here can just as
/// easily mean DCBS silently dropped the title from auto-cart (the exact failure mode
/// this whole app exists to catch) as it can mean the series actually ended. Treat it as
/// a signal for the user's own judgment when deciding what to archive, never as an
/// auto-archive fact.
/// </summary>
public class ClzSeriesSummary
{
    public int Id { get; set; }

    /// <summary>Series name exactly as CLZ has it, for display.</summary>
    public required string Series { get; set; }

    /// <summary>Normalized via TitleNormalizer, matched against PullListEntry.NormalizedTitle - exact match only, deliberately not fuzzy (see docs/BACKLOG.md for why a substring match on common title words produced false positives).</summary>
    public required string NormalizedSeries { get; set; }

    public DateOnly? LastReleaseDate { get; set; }

    public int IssueCount { get; set; }

    /// <summary>When this row's export was imported - every row from one import shares the same timestamp.</summary>
    public DateTime ImportedAt { get; set; }
}
