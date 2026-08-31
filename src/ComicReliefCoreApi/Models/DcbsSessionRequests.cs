using ComicReliefCoreApi.App.Services;

namespace ComicReliefCoreApi.Models;

public record SetDcbsSessionRequest(string Cookie);

public record DcbsSessionStatusResponse(bool HasCookie, DateTime? LastUpdatedAt, DateTime? LastValidatedAt, bool? IsValid)
{
    public static DcbsSessionStatusResponse FromStatus(DcbsSessionStatus status) => new(
        status.HasCookie, status.LastUpdatedAt, status.LastValidatedAt, status.IsValid);
}
