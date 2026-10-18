using System.Collections.Concurrent;
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

    // Confirmed live on the very first real cart (10/18/2026): a plain bidirectional Contains
    // on the full normalized title treats "Amazing Spider-Man #1005" as a match for "Amazing
    // Spider-Man #10", and "Eyrie Magazine #17" for "Eyrie Magazine #1" - Normalize() strips
    // the "#" and the space before it, so the shorter issue number ends up a literal character
    // prefix of the longer one with nothing between the series name and the digits to stop it.
    // When both sides carry an explicit "#<number>", require that number to match exactly
    // before falling back to a (still bidirectional) Contains on the series-name portion alone,
    // which isn't vulnerable to the same digit-adjacency problem.
    private static bool LooksLikeSameItem(string pastedLine, string orderedTitle)
    {
        var pastedIssue = IssueNumberParser.TryParseWholeIssueNumber(pastedLine);
        var orderedIssue = IssueNumberParser.TryParseWholeIssueNumber(orderedTitle);

        if (pastedIssue is not null && orderedIssue is not null)
        {
            if (pastedIssue != orderedIssue)
            {
                return false;
            }

            var pastedSeries = TitleNormalizer.Normalize(IssueNumberParser.TryExtractSeriesTitle(pastedLine) ?? pastedLine);
            var orderedSeries = TitleNormalizer.Normalize(IssueNumberParser.TryExtractSeriesTitle(orderedTitle) ?? orderedTitle);
            if (pastedSeries.Length < MinNormalizedLengthToMatch || orderedSeries.Length < MinNormalizedLengthToMatch)
            {
                return false;
            }
            return pastedSeries.Contains(orderedSeries, StringComparison.Ordinal)
                || orderedSeries.Contains(pastedSeries, StringComparison.Ordinal);
        }

        // At least one side has no "#<number>" to anchor on (a TP/HC/omnibus/magazine without
        // one) - fall back to the original whole-text match; same idiom
        // PullListService.FindKnownOrRecentPurchaseAsync uses for "is this the same item".
        var normalizedPasted = TitleNormalizer.Normalize(pastedLine);
        var normalizedOrdered = TitleNormalizer.Normalize(orderedTitle);
        if (normalizedPasted.Length < MinNormalizedLengthToMatch || normalizedOrdered.Length < MinNormalizedLengthToMatch)
        {
            return false;
        }
        return normalizedPasted.Contains(normalizedOrdered, StringComparison.Ordinal)
            || normalizedOrdered.Contains(normalizedPasted, StringComparison.Ordinal);
    }

    public async Task<IReadOnlyList<CartLineCheckResult>> CheckCartForDuplicatesAsync(
        IReadOnlyList<string> pastedLines, CancellationToken ct = default)
    {
        var allLines = await _store.GetAllFullLinesAsync(ct);
        var solicited = await _solicitationStore.GetAllAsync(ct);
        var byProductCode = solicited.ToDictionary(
            s => s.Item.ProductCode.ToUpperInvariant(), s => s, StringComparer.Ordinal);

        var results = new List<CartLineCheckResult>();
        foreach (var rawLine in pastedLines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var matches = allLines
                .Where(l => LooksLikeSameItem(line, l.Title))
                .Select(l =>
                {
                    byProductCode.TryGetValue(l.ProductCode.ToUpperInvariant(), out var match);
                    return new OrderedItemSearchResult(
                        l.OrderId, l.OrderDate, l.ProductCode, l.Title, l.Quantity, l.UnitPrice, l.Status,
                        match.Item?.ThumbnailUrl ?? l.ThumbnailUrl, match.Item?.ProductUrl, match.Publisher);
                })
                .OrderByDescending(m => m.OrderDate)
                .ToList();

            results.Add(new CartLineCheckResult(rawLine, matches));
        }

        return results;
    }
}
