namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// One line of the most recent cart run through Cart Check - the app can't read DCBS's live
/// cart (see docs/ORDERING-PROCESS.md), so the last cart the user handed it is the closest
/// thing it has, and the Candidates tab uses it to show which candidates are in the cart.
/// Replaced wholesale on every check; every row shares one CheckedAt.
/// </summary>
public class CheckedCartLine
{
    public int Id { get; set; }

    /// <summary>The item's title as read off the cart (or the pasted line, price/qty text and all).</summary>
    public required string Title { get; set; }

    /// <summary>Only known when the cart came from a PDF.</summary>
    public string? ProductCode { get; set; }

    /// <summary>Only known when the cart came from a PDF.</summary>
    public int? Quantity { get; set; }

    public DateTime CheckedAt { get; set; }
}
