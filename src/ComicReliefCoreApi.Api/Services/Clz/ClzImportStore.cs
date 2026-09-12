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

    public async Task<int> UpsertIssuesAsync(IReadOnlyList<ClzIssueRelease> rows, CancellationToken ct = default)
    {
        // Merge by (NormalizedSeries, IssueNumber) rather than wholesale replace - this table
        // has to stay safe to update from a shipment-scoped export (a handful of issues) as
        // well as a full collection export (everything owned), and a wholesale replace from
        // the former would wipe out every other series' history. Confirmed live this session
        // as a real incident: uploading a shipment-only CSV through the old ReplaceAllAsync-
        // style path collapsed the whole collection down to that shipment's ~37 series.
        var existing = await _db.ClzIssueReleases.ToListAsync(ct);
        var byKey = existing.ToDictionary(r => (r.NormalizedSeries, r.IssueNumber));

        foreach (var row in rows)
        {
            if (byKey.TryGetValue((row.NormalizedSeries, row.IssueNumber), out var found))
            {
                found.Series = row.Series;
                found.ReleaseDate = row.ReleaseDate;
                found.ImportedAt = row.ImportedAt;
            }
            else
            {
                _db.ClzIssueReleases.Add(row);
            }
        }

        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task<IReadOnlyList<ClzIssueRelease>> GetAllIssuesAsync(CancellationToken ct = default)
    {
        return await _db.ClzIssueReleases.ToListAsync(ct);
    }
}
