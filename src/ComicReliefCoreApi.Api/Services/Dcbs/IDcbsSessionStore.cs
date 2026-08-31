namespace ComicReliefCoreApi.Api.Services.Dcbs;

/// <summary>
/// Raw storage for the current DCBS session cookie - no judgment calls here (whether a
/// cookie is actually any good lives in ComicReliefCoreApi.App), just get/set against
/// the single-row DcbsSession table so a fresh cookie can be pasted in at runtime
/// instead of requiring a Fly secret update and redeploy every time it expires.
/// </summary>
public interface IDcbsSessionStore
{
    Task<string?> GetCookieAsync(CancellationToken ct = default);

    Task SetCookieAsync(string cookie, CancellationToken ct = default);

    Task MarkValidatedAsync(CancellationToken ct = default);

    Task<DateTime?> GetLastUpdatedAtAsync(CancellationToken ct = default);

    Task<DateTime?> GetLastValidatedAtAsync(CancellationToken ct = default);
}
