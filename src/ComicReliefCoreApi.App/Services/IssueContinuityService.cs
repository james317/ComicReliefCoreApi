using ComicReliefCoreApi.Api.Services;
using ComicReliefCoreApi.Api.Services.Dcbs;

namespace ComicReliefCoreApi.App.Services;

public class IssueContinuityService : IIssueContinuityService
{
    private readonly IPullListService _pullList;
    private readonly IDcbsOrderSnapshotStore _orderStore;

    public IssueContinuityService(IPullListService pullList, IDcbsOrderSnapshotStore orderStore)
    {
        _pullList = pullList;
        _orderStore = orderStore;
    }

    public async Task<IReadOnlyList<MissedIssueFlag>> CheckMissedIssuesAsync(CancellationToken ct = default)
    {
        var entries = await _pullList.GetAllAsync(ct);
        var lines = await _orderStore.GetAllLinesAsync(ct);

        var flags = new List<MissedIssueFlag>();

        foreach (var entry in entries)
        {
            // Match this title's order lines, parse each one's issue number, sort by OrderId
            // ascending (DCBS order ids are sequential, so this is chronological purchase
            // order), and dedupe same-issue-number lines (variant covers of one issue) down
            // to the earliest purchase of that number.
            var points = lines
                .Where(l => TitleNormalizer.IsLikelySeriesMatch(l.Title, entry.Title))
                .Select(l => new
                {
                    l.OrderId,
                    OrderIdNum = long.TryParse(l.OrderId, out var n) ? n : (long?)null,
                    IssueNumber = IssueNumberParser.TryParseWholeIssueNumber(l.Title),
                })
                .Where(p => p.OrderIdNum is not null && p.IssueNumber is not null)
                .OrderBy(p => p.OrderIdNum)
                .GroupBy(p => p.IssueNumber)
                .Select(g => g.First())
                .OrderBy(p => p.OrderIdNum)
                .ToList();

            if (points.Count < 2)
            {
                continue;
            }

            // runMax is the highest issue number seen in the current "run" (volume). A drop
            // of more than 1 below runMax is treated as a new volume/relaunch (e.g. jumping
            // back to #1) rather than a gap - real shipments essentially never regress by more
            // than a single issue out of order, but a relaunch always drops much further, so
            // this threshold tells the two apart without needing volume/series data DCBS's
            // order titles don't carry. A drop of exactly 1 (e.g. #10 arrives after #9 shipped
            // late) is left alone rather than resetting the run, since it's still the same
            // volume and shouldn't blind the check to a real gap right after it.
            var runMax = points[0].IssueNumber!.Value;
            var runMaxOrderId = points[0].OrderId;

            for (var i = 1; i < points.Count; i++)
            {
                var number = points[i].IssueNumber!.Value;
                var orderId = points[i].OrderId;

                if (number == runMax)
                {
                    continue;
                }

                if (number == runMax - 1)
                {
                    // Exactly one behind - same volume, just arrived out of order. Leave runMax alone.
                    continue;
                }

                if (number > runMax + 1)
                {
                    for (var missing = runMax + 1; missing < number; missing++)
                    {
                        flags.Add(new MissedIssueFlag(entry.Title, missing, runMax, runMaxOrderId, number, orderId));
                    }
                }

                // Normal continuation (number == runMax + 1) or a relaunch/new volume
                // (number < runMax - 1) both just advance the run the same way from here.
                runMax = number;
                runMaxOrderId = orderId;
            }
        }

        return flags;
    }
}
