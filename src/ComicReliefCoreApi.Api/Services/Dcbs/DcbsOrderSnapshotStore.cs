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

    public async Task ReplaceAsync(
        string orderId, IReadOnlyList<DcbsOrderLine> lines, DateTime syncedAt, CancellationToken ct = default)
    {
        await _db.DcbsOrderSnapshotLines.ExecuteDeleteAsync(ct);
        _db.DcbsOrderSnapshotLines.AddRange(lines.Select(l => new DcbsOrderSnapshotLine
        {
            OrderId = orderId,
            ProductCode = l.ProductCode,
            Title = l.Title,
            SyncedAt = syncedAt,
        }));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlySet<string>> GetProductCodesAsync(CancellationToken ct = default)
    {
        var codes = await _db.DcbsOrderSnapshotLines.Select(l => l.ProductCode).ToListAsync(ct);
        return codes.Select(c => c.ToUpperInvariant()).ToHashSet();
    }

    public async Task<(string? OrderId, DateTime? SyncedAt, int LineCount)> GetStatusAsync(CancellationToken ct = default)
    {
        var first = await _db.DcbsOrderSnapshotLines.FirstOrDefaultAsync(ct);
        if (first is null)
        {
            return (null, null, 0);
        }
        var count = await _db.DcbsOrderSnapshotLines.CountAsync(ct);
        return (first.OrderId, first.SyncedAt, count);
    }
}
