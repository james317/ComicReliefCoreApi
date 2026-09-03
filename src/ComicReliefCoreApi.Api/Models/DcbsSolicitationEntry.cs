namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// One persisted row from a DCBS publisher-listing crawl (mirrors DcbsListingItem, the raw
/// scrape DTO). Persisted rather than cached in memory so a code deploy - which happens far
/// more often than DCBS's own catalog turns over - doesn't wipe perfectly good crawled data;
/// refreshing is a manual action from the Candidates page, not tied to server lifetime.
/// Replaced one publisher at a time (see IDcbsSolicitationStore), not as one big wipe, so a
/// single publisher's transient crawl failure leaves its last-known-good rows in place
/// instead of losing that publisher's data until the next successful refresh.
/// </summary>
public class DcbsSolicitationEntry
{
    public int Id { get; set; }

    public required string Publisher { get; set; }

    public required string ProductCode { get; set; }

    public required string Title { get; set; }

    public required string ProductUrl { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string? CreatorsAndDescription { get; set; }

    public decimal? Price { get; set; }

    public bool IsRelisted { get; set; }

    public bool IsFacsimileOrReprint { get; set; }

    /// <summary>When this publisher's most recent successful crawl ran - every row from one publisher's refresh shares the same timestamp.</summary>
    public DateTime RefreshedAt { get; set; }
}
