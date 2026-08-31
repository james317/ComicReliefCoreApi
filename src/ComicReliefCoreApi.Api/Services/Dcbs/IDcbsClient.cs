using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

/// <summary>
/// Thin wrapper over the DCBS endpoints reverse-engineered this session. Every method
/// here corresponds to a specific finding in docs/BACKLOG.md - see that file for why
/// each one works the way it does before changing anything here.
/// </summary>
public interface IDcbsClient
{
    Task<IReadOnlyList<DcbsSeriesSearchResult>> SearchSeriesAsync(string term, CancellationToken ct = default);

    /// <summary>
    /// POST /ajax/AddPullListItem. Known to return a raw HTTP 500 (not a graceful JSON
    /// failure) for series using DCBS's newer 12-13 digit codes - callers should treat
    /// any non-success as "try the order-form route instead", not as a final answer.
    /// </summary>
    Task<(bool Success, string RawResponse)> TryAddPullListItemAsync(string seriesCode, string title, int qty = 1, CancellationToken ct = default);

    /// <summary>Fetches and parses the real, persistent pull list - the only trustworthy way to confirm an add actually stuck.</summary>
    Task<IReadOnlyList<DcbsPullListRow>> GetPullListAsync(CancellationToken ct = default);

    /// <summary>Ordered list of every product code on an order's own UpdatePullListFromOrder form (current or already-shipped order).</summary>
    Task<IReadOnlyList<DcbsOrderLine>> GetOrderLinesAsync(string orderId, CancellationToken ct = default);

    /// <summary>
    /// POST /Account/UpdatePullListFromOrder/{orderId}. Requires every product code on
    /// that order's form, not just the target one - DCBS pairs pulllistqty/productcode
    /// fields positionally, so the full set must be resubmitted (untouched rows at qty 0).
    /// </summary>
    Task<bool> TryUpdatePullListFromOrderAsync(
        string orderId,
        IReadOnlyList<string> allProductCodesInOrder,
        string targetProductCode,
        int qty = 1,
        CancellationToken ct = default);

    /// <summary>Recent order ids, newest first, from /account/orders.</summary>
    Task<IReadOnlyList<string>> GetRecentOrderIdsAsync(int max = 6, CancellationToken ct = default);

    /// <summary>
    /// Fetches /account/pulllist and reports whether the current session cookie
    /// actually authenticates - DCBS redirects an unauthenticated/expired session to
    /// its login page rather than returning an error, so this is the real signal to
    /// check after pasting in a fresh cookie.
    /// </summary>
    Task<bool> IsSessionValidAsync(CancellationToken ct = default);
}
