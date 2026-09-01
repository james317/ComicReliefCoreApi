namespace ComicReliefCoreApi.Api.Models.Dcbs;

/// <summary>One row from an /ajax/PullListSearch result.</summary>
public record DcbsSeriesSearchResult(string SeriesCode, string SeriesTitle, string? CurrentIssueText);

/// <summary>One row from the real, persistent /account/pulllist page.</summary>
public record DcbsPullListRow(string Title, int Qty, string PullListId);

/// <summary>One purchased line item as it appears on an order detail page.</summary>
public record DcbsOrderLine(string ProductCode, string Title);

/// <summary>
/// One item from a publisher's current-preorders listing page (/products/&lt;slug&gt;/&lt;id&gt;).
/// Title includes the issue number and variant description as DCBS writes it (e.g. "Absolute
/// Batman #25 Cvr F Jonboy Meyers Glow-In-The-Dark Card Stock Var") - callers needing just the
/// series name should use TitleNormalizer.IsLikelySeriesMatch rather than trying to parse it out
/// here. CreatorsAndDescription is the writer/artist/cover-artist line plus the truncated
/// solicitation blurb shown on the listing page - the full untruncated text only exists on the
/// product page itself (ProductUrl). IsRelisted reflects DCBS's own "Relisted" banner - a small
/// minority of items on an otherwise-current preorders page turned out to carry this (confirmed
/// live 9/2026: 3 of 325 on the DC Comics page), so it's surfaced as a raw fact rather than
/// silently filtered.
/// </summary>
public record DcbsListingItem(
    string ProductCode,
    string Title,
    string ProductUrl,
    string? ThumbnailUrl,
    string? CreatorsAndDescription,
    decimal? Price,
    bool IsRelisted);

/// <summary>One DCBS publisher category page, as linked from the site's own nav.</summary>
public record DcbsPublisherCategory(string Slug, int CategoryId, string DisplayName);
