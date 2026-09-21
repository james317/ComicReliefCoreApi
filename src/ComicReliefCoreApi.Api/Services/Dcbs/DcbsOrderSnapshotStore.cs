using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

public class DcbsOrderSnapshotStore : IDcbsOrderSnapshotStore
{
    private readonly ComicReliefDbContext _db;

    public DcbsOrderSnapshotStore(ComicReliefDbContext db)
    {
        _db = db;
    }

    public async Task UpsertOrderAsync(
        string orderId, IReadOnlyList<DcbsOrderLine> lines, DateOnly? orderDate, DateTime syncedAt, CancellationToken ct = default)
    {
        // Scoped to this one order, not a wholesale wipe - real case this exists for:
        // the user ordered Altered States: Warlords #4 (a different cover) on an earlier
        // order, then a new variant cover got solicited later and their pull-list check
        // only had the single latest order to compare against, so it looked new and got
        // ordered again. Keeping every order on file (not just the latest) is what makes
        // that catchable.
        await _db.DcbsOrderSnapshotLines.Where(l => l.OrderId == orderId).ExecuteDeleteAsync(ct);
        _db.DcbsOrderSnapshotLines.AddRange(lines.Select(l => new DcbsOrderSnapshotLine
        {
            OrderId = orderId,
            ProductCode = l.ProductCode,
            Title = l.Title,
            Status = l.Status,
            SyncedAt = syncedAt,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            ThumbnailUrl = l.ThumbnailUrl,
            OrderDate = orderDate,
        }));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlySet<string>> GetProductCodesAsync(CancellationToken ct = default)
    {
        // Deliberately not scoped to any one order - aggregates product codes across
        // every synced order, which is the entire point (check a new solicitation against
        // everything on file, not just the most recent order).
        var codes = await _db.DcbsOrderSnapshotLines.Select(l => l.ProductCode).ToListAsync(ct);
        return codes.Select(c => c.ToUpperInvariant()).ToHashSet();
    }

    public async Task<(int OrderCount, int TotalLineCount, DateTime? LastSyncedAt)> GetStatusAsync(CancellationToken ct = default)
    {
        var orderCount = await _db.DcbsOrderSnapshotLines.Select(l => l.OrderId).Distinct().CountAsync(ct);
        var totalLineCount = await _db.DcbsOrderSnapshotLines.CountAsync(ct);
        var lastSyncedAt = await _db.DcbsOrderSnapshotLines.MaxAsync(l => (DateTime?)l.SyncedAt, ct);
        return (orderCount, totalLineCount, lastSyncedAt);
    }

    public async Task<IReadOnlyList<(string OrderId, string Title, DcbsShipmentStatus? Status)>> GetAllLinesAsync(CancellationToken ct = default)
    {
        var rows = await _db.DcbsOrderSnapshotLines.Select(l => new { l.OrderId, l.Title, l.Status }).ToListAsync(ct);
        return rows.Select(r => (r.OrderId, r.Title, r.Status)).ToList();
    }

    public async Task<IReadOnlyList<DcbsOrderSnapshotLine>> SearchLinesAsync(string term, CancellationToken ct = default)
    {
        // EF.Functions.Like would push this to SQLite, but SQLite's LIKE is ASCII-only
        // case-insensitive (mishandles non-ASCII titles) - loading and filtering in memory
        // sidesteps that. A personal order history (low hundreds of lines even after years)
        // is nowhere near large enough for this to matter.
        var all = await _db.DcbsOrderSnapshotLines.AsNoTracking().ToListAsync(ct);
        return all
            .Where(l => l.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.OrderDate)
            .ThenBy(l => l.Title)
            .ToList();
    }
}
