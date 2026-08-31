namespace ComicReliefCoreApi.App.Services;

public record DcbsSessionStatus(bool HasCookie, DateTime? LastUpdatedAt, DateTime? LastValidatedAt, bool? IsValid);

public interface IDcbsSessionManager
{
    /// <summary>Stores a newly-pasted cookie and immediately checks whether it actually authenticates, rather than accepting it on faith.</summary>
    Task<DcbsSessionStatus> SetAndValidateAsync(string cookie, CancellationToken ct = default);

    /// <summary>Re-checks the currently stored cookie without changing it - useful for a "check now" button separate from pasting a new one.</summary>
    Task<DcbsSessionStatus> RevalidateAsync(CancellationToken ct = default);

    Task<DcbsSessionStatus> GetStatusAsync(CancellationToken ct = default);
}
