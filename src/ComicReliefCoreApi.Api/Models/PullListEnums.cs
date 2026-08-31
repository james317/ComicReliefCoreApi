namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// Where a title currently stands relative to DCBS's own persistent pull list.
/// </summary>
public enum PullListStatus
{
    /// <summary>Never attempted, or attempted but not yet resolved either way.</summary>
    Unresolved,

    /// <summary>Confirmed present on the real DCBS pull list (verified via /account/pulllist, not just a success response).</summary>
    Sticky,

    /// <summary>Confirmed DCBS will not hold this on its persistent pull list; must be re-added by hand each cycle.</summary>
    Unsticky
}

/// <summary>
/// The physical format a title should be tracked/resolved as, since DCBS can expose
/// separate series records per format (see: Gatchaman TP vs. the single-issue Gatchaman series).
/// </summary>
public enum PullListFormat
{
    Unknown,
    SingleIssue,
    TradePaperback,
    Hardcover
}

/// <summary>Which DCBS mechanism actually produced a given result, for diagnostics.</summary>
public enum PullListAddMethod
{
    /// <summary>POST /ajax/AddPullListItem via a series code found through /ajax/PullListSearch.</summary>
    SearchAndAdd,

    /// <summary>POST /Account/UpdatePullListFromOrder/{orderId}, keyed by an already-purchased product code.</summary>
    OrderForm
}
