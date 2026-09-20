using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Services.Dcbs;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.App.Services;

public class ReviewFlagService : IReviewFlagService
{
    private readonly ComicReliefDbContext _db;
    private readonly IDcbsClient _dcbs;

    public ReviewFlagService(ComicReliefDbContext db, IDcbsClient dcbs)
    {
        _db = db;
        _dcbs = dcbs;
    }

    public async Task<ReviewFlagEntry> FlagAsync(
        string title, string? publisher, string? productCode, string? productUrl, string? notes,
        CancellationToken ct = default)
    {
        var entry = new ReviewFlagEntry
        {
            Title = title,
            Publisher = publisher,
            ProductCode = productCode,
            ProductUrl = productUrl,
            Notes = notes,
        };
        _db.ReviewFlagEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<ReviewFlagEntry>> GetOpenAsync(CancellationToken ct = default)
    {
        return await _db.ReviewFlagEntries
            .Where(e => e.ResolvedAt == null)
            .OrderBy(e => e.FlaggedAt)
            .ToListAsync(ct);
    }

    public async Task<ReviewFlagEntry?> ResolveAsync(int id, CancellationToken ct = default)
    {
        var entry = await _db.ReviewFlagEntries.FindAsync([id], ct);
        if (entry is null)
        {
            return null;
        }
        entry.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public Task<DateOnly?> GetOrderEditCutoffDateAsync(CancellationToken ct = default) =>
        _dcbs.GetOrderEditCutoffDateAsync(ct);
}
