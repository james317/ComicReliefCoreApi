namespace ComicReliefCoreApi.Api.Configuration;

/// <summary>
/// DCBS has no public API - this app acts on the user's own account using a session
/// cookie they extract from their own logged-in browser (see docs/BACKLOG.md for how
/// and why). The cookie itself is stored in the database and settable at runtime via
/// POST /api/dcbs-session (see DcbsSessionStore/DcbsSessionController), not here - it's
/// a forms-auth ticket with a real expiry, so pasting in a fresh one needs to work
/// without a redeploy. This options class is left just for anything genuinely static
/// about talking to DCBS.
/// </summary>
public class DcbsOptions
{
    public string BaseUrl { get; set; } = "https://www.dcbservice.com";
}
