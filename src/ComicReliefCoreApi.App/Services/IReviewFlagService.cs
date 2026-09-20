using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.App.Services;

public interface IReviewFlagService
{
    /// <summary>
    /// Flags a listing title for later review - always creates a new entry rather than
    /// deduping, since re-flagging the same title after resolving it once (e.g. it came back
    /// solicited again next month) is a legitimate new flag, not a repeat of the old one.
    /// </summary>
    Task<ReviewFlagEntry> FlagAsync(
        string title, string? publisher, string? productCode, string? productUrl, string? notes,
        CancellationToken ct = default);

    /// <summary>Every unresolved flag, oldest first - the ones still waiting on a decision.</summary>
    Task<IReadOnlyList<ReviewFlagEntry>> GetOpenAsync(CancellationToken ct = default);

    /// <summary>Marks a flag resolved (the user revisited and decided either way). Returns null if no entry with that id exists.</summary>
    Task<ReviewFlagEntry?> ResolveAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// The current DCBS order-edit cutoff date (see IDcbsClient.GetOrderEditCutoffDateAsync),
    /// passed through so the reminder banner can show "N flagged - edits close in X days"
    /// without the frontend needing its own DCBS access.
    /// </summary>
    Task<DateOnly?> GetOrderEditCutoffDateAsync(CancellationToken ct = default);
}
