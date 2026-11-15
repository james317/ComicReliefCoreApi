using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;
using ComicReliefCoreApi.Api.Services;
using ComicReliefCoreApi.Api.Services.Dcbs;
using Microsoft.Extensions.Logging;

namespace ComicReliefCoreApi.App.Services;

public class OrderSnapshotService : IOrderSnapshotService
{
    // Same reasoning as SolicitationService.MaxConcurrentCrawls - polite to DCBS, still
    // much faster than sequential for the ~20 order pages this account currently has.
    private const int MaxConcurrentOrderFetches = 4;

    private readonly IDcbsClient _dcbs;
    private readonly IDcbsOrderSnapshotStore _store;
    private readonly IDcbsSolicitationStore _solicitationStore;
    private readonly IPullListService _pullList;
    private readonly ILogger<OrderSnapshotService> _logger;

    public OrderSnapshotService(
        IDcbsClient dcbs, IDcbsOrderSnapshotStore store, IDcbsSolicitationStore solicitationStore,
        IPullListService pullList, ILogger<OrderSnapshotService> logger)
    {
        _dcbs = dcbs;
        _store = store;
        _solicitationStore = solicitationStore;
        _pullList = pullList;
        _logger = logger;
    }

    public async Task<OrderSyncResult> SyncRecentAsync(int maxOrders = 24, CancellationToken ct = default)
    {
        var orderIds = await _dcbs.GetRecentOrderIdsAsync(maxOrders, ct);
        var orderDates = await _dcbs.GetOrderDatesAsync(ct);
        var errors = new ConcurrentDictionary<string, string>();
        var fetched = new ConcurrentDictionary<string, IReadOnlyList<DcbsOrderLine>>();
        var syncedAt = DateTime.UtcNow;

        // Fetching concurrently is safe (no shared mutable state); persisting is not - see
        // SolicitationService.RefreshAsync for why writes to the Scoped DbContext have to
        // happen sequentially, one order at a time, after all fetches complete.
        using var throttle = new SemaphoreSlim(MaxConcurrentOrderFetches);
        var tasks = orderIds.Select(async orderId =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                fetched[orderId] = await _dcbs.GetOrderLinesAsync(orderId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch order {OrderId} - leaving its last known lines in place", orderId);
                errors[orderId] = ex.Message;
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);

        foreach (var (orderId, lines) in fetched)
        {
            DateOnly? orderDate = orderDates.TryGetValue(orderId, out var d) ? d : null;
            await _store.UpsertOrderAsync(orderId, lines, orderDate, syncedAt, ct);
        }

        var newFirstIssues = await _pullList.DetectAndTrackNewFirstIssuesAsync(ct);

        var status = await GetStatusAsync(ct);
        return new OrderSyncResult(status, errors.ToDictionary(kv => kv.Key, kv => kv.Value), newFirstIssues);
    }

    public async Task<OrderSnapshotStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var (orderCount, totalLineCount, lastSyncedAt) = await _store.GetStatusAsync(ct);
        return new OrderSnapshotStatus(orderCount, totalLineCount, lastSyncedAt);
    }

    public Task<DcbsOrderDateInfo?> GetMostRecentOrderDateAsync(CancellationToken ct = default) =>
        _dcbs.GetMostRecentOrderDateAsync(ct);

    public async Task<IReadOnlyList<OrderedItemSearchResult>> SearchAsync(string term, CancellationToken ct = default)
    {
        var lines = await _store.SearchLinesAsync(term, ct);
        var solicited = await _solicitationStore.GetAllAsync(ct);
        // Last-write-wins on a duplicate product code (a relisted item can appear more than
        // once across categories) - fine here, any one match is equally good for a details
        // lookup, unlike SolicitationService's own matching which cares about every listing.
        var byProductCode = solicited.ToDictionary(
            s => s.Item.ProductCode.ToUpperInvariant(), s => s, StringComparer.Ordinal);

        return lines.Select(l =>
        {
            byProductCode.TryGetValue(l.ProductCode.ToUpperInvariant(), out var match);
            return new OrderedItemSearchResult(
                l.OrderId,
                l.OrderDate,
                l.ProductCode,
                l.Title,
                l.Quantity,
                l.UnitPrice,
                l.Status,
                match.Item?.ThumbnailUrl ?? l.ThumbnailUrl,
                match.Item?.ProductUrl,
                match.Publisher);
        }).ToList();
    }

    // Below this length a normalized fragment ("1", "Qty", a lone cover letter) is a substring
    // of nearly every title on file - matching it would flood every pasted line with bogus
    // duplicates instead of the real ones. Real series/issue titles normalize to well past
    // this; nothing legitimate gets excluded by it.
    private const int MinNormalizedLengthToMatch = 4;

    // DCBS tags some volumes with their launch year - "Vampirella (2026) #10" - but not
    // consistently: the same 2026 run also appears as plain "Vampirella #6". So the year is
    // dropped from the series name, and volumes are told apart by RunStartOrderId instead.
    private static readonly Regex TrailingYearRegex = new(@" (19|20)\d{2}$", RegexOptions.Compiled);

    private static string SeriesKey(string title)
    {
        var series = TitleNormalizer.NormalizeKeepingWordBoundaries(IssueNumberParser.TryExtractSeriesTitle(title) ?? title);
        return TrailingYearRegex.Replace(series, "");
    }

    private static long OrderIdNumber(DcbsOrderSnapshotLine line) =>
        long.TryParse(line.OrderId, out var n) ? n : 0;

    // Where the series' current volume begins in order history: the last time its issue
    // numbers dropped back to #1 or #2 (a relaunch). Confirmed live 11/15/2026: matching across
    // volumes flagged "Vampirella (2026) #10" as a duplicate of the 2025 run's #10 (and #9 the
    // month before) - the series relaunched at #1 in February 2026, so nothing ordered before
    // that can be the same book. Only a drop to a low number counts, so a late variant reorder
    // of an older issue doesn't split the run. Returns null when the pasted issue is itself a
    // relaunch (#1/#2 against a run already past that), where nothing on file can match it.
    private const int RelaunchMaxIssue = 2;

    private static bool IsRelaunch(int issue, int runMax) => issue <= RelaunchMaxIssue && issue < runMax - 1;

    private static long? RunStartOrderId(IReadOnlyList<DcbsOrderSnapshotLine> seriesHistory, int pastedIssue)
    {
        long runStart = 0;
        int? runMax = null;
        foreach (var line in seriesHistory
            .OrderBy(OrderIdNumber)
            .ThenBy(l => IssueNumberParser.TryParseWholeIssueNumber(l.Title)))
        {
            var n = IssueNumberParser.TryParseWholeIssueNumber(line.Title)!.Value;
            if (runMax is { } max && IsRelaunch(n, max))
            {
                runStart = OrderIdNumber(line);
                runMax = n;
            }
            else
            {
                runMax = Math.Max(runMax ?? n, n);
            }
        }
        return runMax is { } currentMax && IsRelaunch(pastedIssue, currentMax) ? null : runStart;
    }

    // Lines with a "#<number>" match on series name plus exact issue number, within the series'
    // current run. An exact issue number is required because Normalize() would otherwise make
    // "#10" a character prefix of "#1005" (confirmed live 10/18/2026), and an exact series name
    // because a looser prefix match let "Superman" pair with "Batman Superman Worlds Finest" and
    // "Vampirella" with "Vampirella Paradise". Cancelled lines never count - you don't have them.
    private static IEnumerable<DcbsOrderSnapshotLine> FindAlreadyOrdered(string pastedLine, IReadOnlyList<DcbsOrderSnapshotLine> allLines)
    {
        var live = allLines.Where(l => l.Status != DcbsShipmentStatus.Cancelled).ToList();
        var pastedIssue = IssueNumberParser.TryParseWholeIssueNumber(pastedLine);
        if (pastedIssue is null)
        {
            // A TP/HC/omnibus/magazine with no "#<number>" - whole-text match, same idiom
            // PullListService.FindKnownOrRecentPurchaseAsync uses for "is this the same item".
            var normalizedPasted = TitleNormalizer.Normalize(pastedLine);
            if (normalizedPasted.Length < MinNormalizedLengthToMatch)
            {
                return [];
            }
            return live.Where(l =>
            {
                var normalizedOrdered = TitleNormalizer.Normalize(l.Title);
                return normalizedOrdered.Length >= MinNormalizedLengthToMatch
                    && (normalizedPasted.Contains(normalizedOrdered, StringComparison.Ordinal)
                        || normalizedOrdered.Contains(normalizedPasted, StringComparison.Ordinal));
            });
        }

        var seriesKey = SeriesKey(pastedLine);
        if (seriesKey.Length < MinNormalizedLengthToMatch)
        {
            return [];
        }
        var seriesHistory = live
            .Where(l => IssueNumberParser.TryParseWholeIssueNumber(l.Title) is not null && SeriesKey(l.Title) == seriesKey)
            .ToList();
        if (RunStartOrderId(seriesHistory, pastedIssue.Value) is not { } runStart)
        {
            return [];
        }
        return seriesHistory.Where(l =>
            IssueNumberParser.TryParseWholeIssueNumber(l.Title) == pastedIssue && OrderIdNumber(l) >= runStart);
    }

    public Task<IReadOnlyList<CartLineCheckResult>> CheckCartForDuplicatesAsync(
        IReadOnlyList<string> pastedLines, CancellationToken ct = default) =>
        CheckCartLinesAsync(pastedLines.Select(l => (l, (string?)null, (int?)null)).ToList(), ct);

    public async Task<IReadOnlyList<CartLineCheckResult>> CheckCartPdfAsync(Stream pdf, CancellationToken ct = default)
    {
        var items = DcbsCartPdfParser.Parse(pdf);
        return await CheckCartLinesAsync(items.Select(i => (i.Title, (string?)i.ProductCode, i.Quantity)).ToList(), ct);
    }

    private async Task<IReadOnlyList<CartLineCheckResult>> CheckCartLinesAsync(
        IReadOnlyList<(string Line, string? ProductCode, int? Quantity)> cartLines, CancellationToken ct)
    {
        var allLines = await _store.GetAllFullLinesAsync(ct);
        var solicited = await _solicitationStore.GetAllAsync(ct);
        var byProductCode = solicited.ToDictionary(
            s => s.Item.ProductCode.ToUpperInvariant(), s => s, StringComparer.Ordinal);

        var results = new List<CartLineCheckResult>();
        foreach (var (rawLine, productCode, quantity) in cartLines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var matches = FindAlreadyOrdered(line, allLines)
                .Select(l =>
                {
                    byProductCode.TryGetValue(l.ProductCode.ToUpperInvariant(), out var match);
                    return new OrderedItemSearchResult(
                        l.OrderId, l.OrderDate, l.ProductCode, l.Title, l.Quantity, l.UnitPrice, l.Status,
                        match.Item?.ThumbnailUrl ?? l.ThumbnailUrl, match.Item?.ProductUrl, match.Publisher);
                })
                .OrderByDescending(m => m.OrderDate)
                .ToList();

            results.Add(new CartLineCheckResult(rawLine, matches, productCode, quantity));
        }

        return results;
    }
}
