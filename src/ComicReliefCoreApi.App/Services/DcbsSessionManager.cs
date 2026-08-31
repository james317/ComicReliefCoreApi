using ComicReliefCoreApi.Api.Services.Dcbs;

namespace ComicReliefCoreApi.App.Services;

public class DcbsSessionManager : IDcbsSessionManager
{
    private readonly IDcbsClient _dcbs;
    private readonly IDcbsSessionStore _store;

    public DcbsSessionManager(IDcbsClient dcbs, IDcbsSessionStore store)
    {
        _dcbs = dcbs;
        _store = store;
    }

    public async Task<DcbsSessionStatus> SetAndValidateAsync(string cookie, CancellationToken ct = default)
    {
        await _store.SetCookieAsync(cookie, ct);
        return await RevalidateAsync(ct);
    }

    public async Task<DcbsSessionStatus> RevalidateAsync(CancellationToken ct = default)
    {
        var isValid = await _dcbs.IsSessionValidAsync(ct);
        if (isValid)
        {
            await _store.MarkValidatedAsync(ct);
        }

        return await GetStatusAsync(ct) with { IsValid = isValid };
    }

    public async Task<DcbsSessionStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var cookie = await _store.GetCookieAsync(ct);
        var updatedAt = await _store.GetLastUpdatedAtAsync(ct);
        var validatedAt = await _store.GetLastValidatedAtAsync(ct);
        return new DcbsSessionStatus(
            HasCookie: !string.IsNullOrWhiteSpace(cookie),
            LastUpdatedAt: updatedAt,
            LastValidatedAt: validatedAt,
            IsValid: null);
    }
}
