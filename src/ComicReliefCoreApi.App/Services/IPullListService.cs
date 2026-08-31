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
}
