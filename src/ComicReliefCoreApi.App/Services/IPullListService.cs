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
}

public sealed record PullListImportRow(string Title, PullListStatus Status, string? Notes);
