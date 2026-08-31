namespace ComicReliefCoreApi.Api.Models;

/// <summary>
/// Single-row table holding the current DCBS session cookie, settable at runtime via
/// the UI rather than only through a Fly secret + redeploy. DCBS has no public API and
/// the cookie is a forms-auth ticket with a real expiry, so this needs to be refreshed
/// periodically by pasting a freshly-captured cookie - see docs/BACKLOG.md for how to
/// capture one from a logged-in browser.
/// </summary>
public class DcbsSession
{
    /// <summary>Always 1 - this is a single-row settings table, not a per-user table (this app has exactly one user).</summary>
    public int Id { get; set; }

    public string? SessionCookie { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Last time the cookie was confirmed to actually authenticate against DCBS, distinct from when it was merely pasted in.</summary>
    public DateTime? LastValidatedAt { get; set; }
}
