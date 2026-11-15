using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

/// <summary>The most recent cart run through Cart Check - see CheckedCartLine.</summary>
public interface ICheckedCartStore
{
    /// <summary>Replaces whatever cart was saved before with this one.</summary>
    Task ReplaceAsync(IReadOnlyList<CheckedCartLine> lines, CancellationToken ct = default);

    /// <summary>The saved cart's lines - empty if no cart has been checked yet.</summary>
    Task<IReadOnlyList<CheckedCartLine>> GetAsync(CancellationToken ct = default);
}

public class CheckedCartStore : ICheckedCartStore
{
    private readonly ComicReliefDbContext _db;

    public CheckedCartStore(ComicReliefDbContext db)
    {
        _db = db;
    }

    public async Task ReplaceAsync(IReadOnlyList<CheckedCartLine> lines, CancellationToken ct = default)
    {
        await _db.CheckedCartLines.ExecuteDeleteAsync(ct);
        _db.CheckedCartLines.AddRange(lines);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CheckedCartLine>> GetAsync(CancellationToken ct = default) =>
        await _db.CheckedCartLines.AsNoTracking().ToListAsync(ct);
}
