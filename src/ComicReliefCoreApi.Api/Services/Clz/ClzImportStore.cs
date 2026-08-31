using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Api.Services.Clz;

public class ClzImportStore : IClzImportStore
{
    private readonly ComicReliefDbContext _db;

    public ClzImportStore(ComicReliefDbContext db)
    {
        _db = db;
    }

    public async Task<int> ReplaceAllAsync(IReadOnlyList<ClzSeriesSummary> rows, CancellationToken ct = default)
    {
        // ExecuteDeleteAsync issues a single DELETE rather than loading every row into
        // memory first - fine for ~500 series, but also just the right tool regardless.
        await _db.ClzSeriesSummaries.ExecuteDeleteAsync(ct);
        _db.ClzSeriesSummaries.AddRange(rows);
        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<ClzImportStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var count = await _db.ClzSeriesSummaries.CountAsync(ct);
        if (count == 0)
        {
            return new ClzImportStatus(false, 0, null);
        }

        var importedAt = await _db.ClzSeriesSummaries.Select(s => s.ImportedAt).MaxAsync(ct);
        return new ClzImportStatus(true, count, importedAt);
    }

    public async Task<IReadOnlyDictionary<string, ClzSeriesSummary>> GetAllByNormalizedSeriesAsync(CancellationToken ct = default)
    {
        var all = await _db.ClzSeriesSummaries.ToListAsync(ct);
        return all.ToDictionary(s => s.NormalizedSeries);
    }
}
