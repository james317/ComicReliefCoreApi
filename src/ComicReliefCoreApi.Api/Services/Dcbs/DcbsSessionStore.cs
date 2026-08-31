using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

public class DcbsSessionStore : IDcbsSessionStore
{
    private const int SingletonRowId = 1;

    private readonly ComicReliefDbContext _db;

    public DcbsSessionStore(ComicReliefDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetCookieAsync(CancellationToken ct = default)
    {
        var row = await _db.DcbsSessions.FindAsync(new object?[] { SingletonRowId }, ct);
        return row?.SessionCookie;
    }

    public async Task SetCookieAsync(string cookie, CancellationToken ct = default)
    {
        var row = await GetOrCreateRowAsync(ct);
        row.SessionCookie = cookie;
        row.UpdatedAt = DateTime.UtcNow;
        row.LastValidatedAt = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkValidatedAsync(CancellationToken ct = default)
    {
        var row = await GetOrCreateRowAsync(ct);
        row.LastValidatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DateTime?> GetLastUpdatedAtAsync(CancellationToken ct = default)
    {
        var row = await _db.DcbsSessions.FindAsync(new object?[] { SingletonRowId }, ct);
        return row?.UpdatedAt;
    }

    public async Task<DateTime?> GetLastValidatedAtAsync(CancellationToken ct = default)
    {
        var row = await _db.DcbsSessions.FindAsync(new object?[] { SingletonRowId }, ct);
        return row?.LastValidatedAt;
    }

    private async Task<DcbsSession> GetOrCreateRowAsync(CancellationToken ct)
    {
        var row = await _db.DcbsSessions.FindAsync(new object?[] { SingletonRowId }, ct);
        if (row is not null)
        {
            return row;
        }

        row = new DcbsSession { Id = SingletonRowId };
        _db.DcbsSessions.Add(row);
        return row;
    }
}
