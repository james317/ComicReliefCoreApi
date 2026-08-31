using ComicReliefCoreApi.Api.Services.Clz;

namespace ComicReliefCoreApi.App.Services;

public interface IClzCollectionService
{
    /// <summary>Parses the uploaded CSV and replaces the stored collection snapshot entirely. Returns the number of distinct series imported.</summary>
    Task<int> ImportAsync(Stream csvStream, CancellationToken ct = default);

    Task<ClzImportStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// The last-owned-issue date for each given normalized pull-list title, matched by
    /// exact normalized-title equality only - deliberately not fuzzy/substring matching.
    /// A quick substring-based cross-reference attempt this session produced real false
    /// positives (e.g. "Batman" matching an unrelated one-shot "Archie Meets Batman 66"
    /// just because the words overlap), so titles with no exact CLZ match are simply
    /// absent from the result rather than guessed at.
    /// </summary>
    Task<IReadOnlyDictionary<string, DateOnly?>> GetLastKnownIssueDatesAsync(
        IEnumerable<string> normalizedTitles, CancellationToken ct = default);
}
