namespace ComicReliefCoreApi.Api.Configuration;

/// <summary>
/// DCBS has no public API - this app acts on the user's own account using a session
/// cookie they extract from their own logged-in browser (see docs/BACKLOG.md for how
/// and why). The cookie is a forms-auth ticket with a real expiry, so it needs manual
/// refreshing periodically; there is no login flow here.
/// </summary>
public class DcbsOptions
{
    public string BaseUrl { get; set; } = "https://www.dcbservice.com";

    /// <summary>
    /// The full "name=value; name2=value2" Cookie header captured from a logged-in
    /// browser session. Set via the DCBS__SessionCookie environment variable / Fly
    /// secret - never commit a real value to appsettings.json or the repo.
    /// </summary>
    public string SessionCookie { get; set; } = "";
}
