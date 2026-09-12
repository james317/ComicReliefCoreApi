namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// One row per owned issue from a CLZ export - unlike ClzSeriesSummary (aggregated to one
/// row per series, losing per-issue detail), this keeps every issue's own release date, for
/// matching a specific shipment line (a specific issue number) to when it actually came out
/// rather than only knowing the series' latest release overall.
/// </summary>
public class ClzIssueRelease
{
    public int Id { get; set; }

    /// <summary>Series name exactly as CLZ has it, for display.</summary>
    public required string Series { get; set; }

    /// <summary>Normalized via TitleNormalizer - matched against a shipment line's title the same way ClzSeriesSummary matches pull-list titles.</summary>
    public required string NormalizedSeries { get; set; }

    /// <summary>Leading numeric part of CLZ's own "Issue" column (e.g. "1101A" -&gt; 1101, "3D" -&gt; 3) - the same whole-number issue identity IssueNumberParser extracts from a DCBS title, so the two can be compared directly.</summary>
    public int IssueNumber { get; set; }

    public DateOnly? ReleaseDate { get; set; }

    /// <summary>When this row's export was imported - every row from one import shares the same timestamp.</summary>
    public DateTime ImportedAt { get; set; }
}
