using ComicReliefCoreApi.Api.Models.Dcbs;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

/// <summary>Raw persisted facts about the last crawl - no matching/business logic, see ISolicitationService (.App) for that.</summary>
public interface IDcbsSolicitationStore
{
    /// <summary>Replaces only this publisher's rows - a failed crawl for a different publisher never touches these.</summary>
    Task ReplacePublisherAsync(
        string publisher, IReadOnlyList<DcbsListingItem> items, DateTime refreshedAt, CancellationToken ct = default);

    Task<IReadOnlyList<(string Publisher, DcbsListingItem Item)>> GetAllAsync(CancellationToken ct = default);

    Task<(DateTime? LastRefreshedAt, IReadOnlyDictionary<string, int> PublisherItemCounts)> GetStatusAsync(CancellationToken ct = default);
}
