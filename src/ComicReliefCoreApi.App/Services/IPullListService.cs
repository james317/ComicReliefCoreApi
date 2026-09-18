using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.App.Services;

public interface IPullListService
{
    /// <summary>
    /// Given a title, tries to get it onto DCBS's real persistent pull list (trying the
    /// direct search-and-add route first, falling back to the order-form route when
    /// that fails or the code looks likely to trigger DCBS's known overflow bug), and
    /// falls back to marking it Unsticky with a specific reason when neither works.
    /// Always ends by re-verifying against the live pull list - never trusts a success
    /// response alone.
    /// </summary>
    Task<PullListEntry> AddToPullListAsync(string title, CancellationToken ct = default);

    /// <summary>
    /// Normalized titles of everything currently tracked (any status), for callers that
    /// need to cross-reference some other title list against the pull list - e.g. the
    /// solicitations feed badging titles the user already tracks. Callers should use
    /// <see cref="TitleNormalizer"/> on their own titles before comparing, rather than
    /// re-deriving DCBS's naming-inconsistency rules themselves.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetTrackedNormalizedTitlesAsync(CancellationToken ct = default);

    /// <summary>
    /// Seeds entries with an already-known status, without calling DCBS - for importing
    /// docs/pull-list.csv, whose Sticky/Unsticky values were themselves captured from
    /// DCBS's own live sticky/unsticky pull lists rather than guessed, so re-running the
    /// full <see cref="AddToPullListAsync"/> workflow would just be re-confirming already-known
    /// facts. Skips any title that already has an entry (by normalized title) rather than
    /// overwriting something the app has already resolved for itself. Returns the count
    /// actually inserted.
    /// </summary>
    Task<int> ImportKnownEntriesAsync(IEnumerable<PullListImportRow> rows, CancellationToken ct = default);

    /// <summary>
    /// Marks a title archived (hidden from the default pull-list view) or unarchived.
    /// Purely a display decision - never touches DCBS. Returns null if no entry with that id exists.
    /// </summary>
    Task<PullListEntry?> SetArchivedAsync(int id, bool archived, CancellationToken ct = default);

    /// <summary>Every non-archived tracked entry, any status - for callers cross-referencing the whole pull list against some other data source (e.g. issue-continuity checks against order history). Archived titles are excluded, same as the default pull-list view - a series the user marked done shouldn't keep getting flagged.</summary>
    Task<IReadOnlyList<PullListEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Scans every synced order line for a "#1" from a series not already tracked, and
    /// immediately runs it through <see cref="AddToPullListAsync"/> (sticky first, falling
    /// back to unsticky) instead of waiting for the gap to be noticed by hand - the real
    /// case this closes: "You'll Never Leave This Place Alive" #1 was ordered 8/29/2026 but
    /// never explicitly added, and sat as "seen in orders only, not on either list" until
    /// someone caught it.
    ///
    /// A line whose extracted series title reads as a one-shot/special ("One-Shot" or
    /// "Special" as its own word) is left Unresolved instead of auto-added, matching the
    /// existing pull-list.csv convention of not persistently tracking real one-shots (e.g.
    /// "Vampirella X Witchblade Special") since there's no next issue to keep pulling. This
    /// is a title-text heuristic only - DCBS's order lines carry no real format flag - so a
    /// one-shot with no such cue in its title (e.g. "Devils Due Presents Lovebunny & Mr
    /// Hell") still gets auto-tracked as if ongoing; archiving the resulting entry
    /// (<see cref="SetArchivedAsync"/>) is the fix once that's noticed, rather than chasing a
    /// perfect heuristic.
    ///
    /// Every line evaluated here - tracked, auto-added, or skipped as a likely one-shot -
    /// ends up with a <see cref="PullListEntry"/> (Unresolved for the skipped case), so a
    /// title is only ever evaluated once across repeated calls rather than re-flagged on
    /// every future sync. Cancelled lines are ignored - never actually kept.
    /// </summary>
    Task<IReadOnlyList<NewFirstIssueDetection>> DetectAndTrackNewFirstIssuesAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches DCBS's own real, persistent /account/pulllist (every series actually sticky
    /// on the account, not just what this app has ever been told about) and inserts a
    /// Sticky PullListEntry for any title with no existing tracked entry - discovered live
    /// 9/2026 when the account's real list turned out to have 236 series against this app's
    /// 122 tracked rows, a gap large enough to explain otherwise-mysterious TP/HC/Omnibus
    /// items showing up on a month's pull-list-order review (an old, long-finished series
    /// left sticky on DCBS from before this app existed keeps matching new collected
    /// editions of itself even once it has nothing new to solicit as single issues).
    /// Each discovered entry is created already Sticky/verified (DCBS's own page is the
    /// verification), never guessed. Returns only the newly-created entries, not the full
    /// reconciled list, so a caller can review exactly what was previously invisible.
    /// </summary>
    Task<IReadOnlyList<PullListEntry>> ReconcileWithDcbsAsync(CancellationToken ct = default);
}

public sealed record PullListImportRow(string Title, PullListStatus Status, string? Notes);

public sealed record NewFirstIssueDetection(string Title, string OrderId, PullListStatus Status, string? Note);
