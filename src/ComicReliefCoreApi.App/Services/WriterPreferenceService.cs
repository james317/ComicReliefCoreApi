using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.App.Services;

public class WriterPreferenceService : IWriterPreferenceService
{
    private readonly ComicReliefDbContext _db;

    public WriterPreferenceService(ComicReliefDbContext db)
    {
        _db = db;
    }

    public async Task<WriterPreference> UpsertAsync(string name, WriterPreferenceType type, CancellationToken ct = default)
    {
        var normalized = TitleNormalizer.Normalize(name);
        var existing = await _db.WriterPreferences.SingleOrDefaultAsync(w => w.NormalizedName == normalized, ct);
        if (existing is not null)
        {
            existing.Type = type;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var entry = new WriterPreference { Name = name, NormalizedName = normalized, Type = type };
        _db.WriterPreferences.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<WriterPreference>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.WriterPreferences.OrderBy(w => w.Name).ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entry = await _db.WriterPreferences.FindAsync([id], ct);
        if (entry is null)
        {
            return false;
        }
        _db.WriterPreferences.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
