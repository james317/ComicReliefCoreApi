namespace ComicReliefCoreApi.Api.Models.Dcbs;

/// <summary>One row from an /ajax/PullListSearch result.</summary>
public record DcbsSeriesSearchResult(string SeriesCode, string SeriesTitle, string? CurrentIssueText);

/// <summary>One row from the real, persistent /account/pulllist page.</summary>
public record DcbsPullListRow(string Title, int Qty, string PullListId);

/// <summary>One purchased line item as it appears on an order detail page.</summary>
public record DcbsOrderLine(string ProductCode, string Title);
