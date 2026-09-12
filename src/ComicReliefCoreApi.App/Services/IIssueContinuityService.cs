namespace ComicReliefCoreApi.App.Services;

public interface IIssueContinuityService
{
    /// <summary>
    /// Cross-references every synced order line against the pull list to flag issue-number
    /// gaps (e.g. owning #12 and #14 of a series but never #13). Mirrors a manual process the
    /// user already runs on every shipment - a "next expected issue" note, incremented on
    /// receipt, investigated whenever a received issue skips ahead - so this only surfaces
    /// candidates for that same investigation; it never marks anything missing on its own.
    /// </summary>
    Task<IReadOnlyList<MissedIssueFlag>> CheckMissedIssuesAsync(CancellationToken ct = default);
}

/// <summary>One suspected gap: PullListTitle is missing MissingIssueNumber, sitting between the PrecedingIssueNumber and FollowingIssueNumber actually on file (with the orders they came from, for the user's own verification step).</summary>
public sealed record MissedIssueFlag(
    string PullListTitle,
    int MissingIssueNumber,
    int PrecedingIssueNumber,
    string PrecedingOrderId,
    int FollowingIssueNumber,
    string FollowingOrderId);
