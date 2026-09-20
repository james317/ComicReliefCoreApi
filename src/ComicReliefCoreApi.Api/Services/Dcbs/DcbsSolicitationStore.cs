using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

public class DcbsSolicitationStore : IDcbsSolicitationStore
{
    private readonly ComicReliefDbContext _db;

    public DcbsSolicitationStore(ComicReliefDbContext db)
    {
        _db = db;
    }

    public async Task ReplacePublisherAsync(
        string publisher, IReadOnlyList<DcbsListingItem> items, DateTime refreshedAt, CancellationToken ct = default)
    {
        // FirstSeenAt has to survive this replace - grab whatever's already on file (keyed by
        // product code, this publisher's rows only) before deleting, so a title that's been
        // solicited for months doesn't look "new" again just because it went through another
        // wipe-and-reinsert cycle.
        var previousFirstSeen = await _db.DcbsSolicitationEntries
            .Where(e => e.Publisher == publisher)
            .Select(e => new { e.ProductCode, e.FirstSeenAt })
            .ToDictionaryAsync(e => e.ProductCode, e => e.FirstSeenAt, ct);

        await _db.DcbsSolicitationEntries.Where(e => e.Publisher == publisher).ExecuteDeleteAsync(ct);
        _db.DcbsSolicitationEntries.AddRange(items.Select(i => new DcbsSolicitationEntry
        {
            Publisher = publisher,
            ProductCode = i.ProductCode,
            Title = i.Title,
            ProductUrl = i.ProductUrl,
            ThumbnailUrl = i.ThumbnailUrl,
            CreatorsAndDescription = i.CreatorsAndDescription,
            Price = i.Price,
            IsRelisted = i.IsRelisted,
            IsFacsimileOrReprint = i.IsFacsimileOrReprint,
            RefreshedAt = refreshedAt,
            FirstSeenAt = previousFirstSeen.GetValueOrDefault(i.ProductCode, refreshedAt),
        }));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(string Publisher, DcbsListingItem Item, DateTime FirstSeenAt, DateTime RefreshedAt)>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await _db.DcbsSolicitationEntries.AsNoTracking().ToListAsync(ct);
        return rows
            .Select(r => (r.Publisher, new DcbsListingItem(
                r.ProductCode, r.Title, r.ProductUrl, r.ThumbnailUrl, r.CreatorsAndDescription, r.Price,
                r.IsRelisted, r.IsFacsimileOrReprint), r.FirstSeenAt, r.RefreshedAt))
            .ToList();
    }

    public async Task<(DateTime? LastRefreshedAt, IReadOnlyDictionary<string, int> PublisherItemCounts)> GetStatusAsync(
        CancellationToken ct = default)
    {
        var lastRefreshedAt = await _db.DcbsSolicitationEntries.MaxAsync(e => (DateTime?)e.RefreshedAt, ct);
        var counts = await _db.DcbsSolicitationEntries
            .GroupBy(e => e.Publisher)
            .Select(g => new { Publisher = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return (lastRefreshedAt, counts.ToDictionary(c => c.Publisher, c => c.Count));
    }
}
