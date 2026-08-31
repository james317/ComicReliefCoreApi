using ComicReliefCoreApi.Api.Data;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Services.Dcbs;
using Microsoft.EntityFrameworkCore;

namespace ComicReliefCoreApi.App.Services;

public class PullListService : IPullListService
{
    // DCBS's newer series codes (12-13 digit GTIN/UPC-style) crash /ajax/AddPullListItem
    // with a raw HTTP 500, almost certainly an Int32 overflow server-side - confirmed
    // this session by testing a short legacy code (succeeded) against several long ones
    // (all 500'd). Anything past Int32.MaxValue skips the direct route entirely rather
    // than wasting a request we already know will fail.
    private const long MaxSafeSeriesCode = int.MaxValue;

    private const int RecentOrdersToScan = 6;

    private readonly ComicReliefDbContext _db;
    private readonly IDcbsClient _dcbs;
    private readonly ILogger<PullListService> _logger;

    public PullListService(ComicReliefDbContext db, IDcbsClient dcbs, ILogger<PullListService> logger)
    {
        _db = db;
        _dcbs = dcbs;
        _logger = logger;
    }

    public async Task<PullListEntry> AddToPullListAsync(string title, CancellationToken ct = default)
    {
        var normalized = TitleNormalizer.Normalize(title);

        var entry = await _db.PullListEntries
            .Include(e => e.Attempts)
            .FirstOrDefaultAsync(e => e.NormalizedTitle == normalized, ct);

        if (entry is null)
        {
            entry = new PullListEntry { Title = title, NormalizedTitle = normalized };
            _db.PullListEntries.Add(entry);
        }

        if (entry.Status == PullListStatus.Sticky && entry.LastVerifiedStickyAt is { } verifiedAt
            && DateTime.UtcNow - verifiedAt < TimeSpan.FromDays(1))
        {
            _logger.LogInformation("{Title} already confirmed sticky as of {VerifiedAt}, skipping", title, verifiedAt);
            return entry;
        }

        entry.LastAttemptedAt = DateTime.UtcNow;

        // Route 1: search DCBS's own series index and try adding directly.
        var searchResults = await _dcbs.SearchSeriesAsync(title, ct);
        var exactMatch = searchResults.FirstOrDefault(r => TitleNormalizer.Normalize(r.SeriesTitle) == normalized);

        if (exactMatch is not null)
        {
            entry.DcbsSeriesCode = exactMatch.SeriesCode;

            var codeLooksSafe = long.TryParse(exactMatch.SeriesCode, out var codeValue) && codeValue <= MaxSafeSeriesCode;
            if (codeLooksSafe)
            {
                var (success, raw) = await _dcbs.TryAddPullListItemAsync(exactMatch.SeriesCode, exactMatch.SeriesTitle, ct: ct);
                entry.Attempts.Add(new PullListAddAttempt
                {
                    Method = PullListAddMethod.SearchAndAdd,
                    Success = success,
                    RawResponse = Truncate(raw),
                });

                if (success && await ConfirmStickyAsync(entry, ct))
                {
                    entry.Status = PullListStatus.Sticky;
                    entry.LastSuccessfulMethod = PullListAddMethod.SearchAndAdd;
                    entry.FailureReason = null;
                    await _db.SaveChangesAsync(ct);
                    return entry;
                }
            }
            else
            {
                _logger.LogInformation(
                    "{Title} series code {Code} looks unsafe for AddPullListItem (matches the known overflow pattern), skipping straight to the order-form route",
                    title, exactMatch.SeriesCode);
            }
        }

        // Route 2: fall back to an already-purchased product code, via the order-form
        // route - this is the one confirmed to work regardless of series code length,
        // and also the more format-aware of the two (see: Gatchaman TP vs. Gatchaman).
        var purchase = await FindKnownOrRecentPurchaseAsync(entry, normalized, ct);
        if (purchase is { } found)
        {
            var lines = await _dcbs.GetOrderLinesAsync(found.OrderId, ct);
            var allCodes = lines.Select(l => l.ProductCode).ToList();

            var orderFormAttempted = await _dcbs.TryUpdatePullListFromOrderAsync(
                found.OrderId, allCodes, found.ProductCode, ct: ct);
            entry.Attempts.Add(new PullListAddAttempt
            {
                Method = PullListAddMethod.OrderForm,
                Success = orderFormAttempted,
                Notes = $"order {found.OrderId}, product {found.ProductCode}",
            });

            entry.LastKnownProductCode = found.ProductCode;
            entry.LastKnownOrderId = found.OrderId;

            if (orderFormAttempted && await ConfirmStickyAsync(entry, ct))
            {
                entry.Status = PullListStatus.Sticky;
                entry.LastSuccessfulMethod = PullListAddMethod.OrderForm;
                entry.FailureReason = null;
                await _db.SaveChangesAsync(ct);
                return entry;
            }
        }

        // Neither route worked - fall back to the external unsticky list, with a
        // specific reason rather than a generic "failed".
        entry.Status = PullListStatus.Unsticky;
        entry.FailureReason = exactMatch is null
            ? "No matching series found in DCBS's pull-list search, and no recent purchase to resolve via the order form."
            : purchase is null
                ? $"Series found ({exactMatch.SeriesCode}) but AddPullListItem did not stick, and no recent purchase was available to try the order-form route."
                : "Series found and a purchase was available, but neither route produced a confirmed sticky entry.";

        await _db.SaveChangesAsync(ct);
        return entry;
    }

    /// <summary>Re-fetches the real pull list and only trusts presence there - both a JSON success and a silent UI have been shown to lie this session.</summary>
    private async Task<bool> ConfirmStickyAsync(PullListEntry entry, CancellationToken ct)
    {
        var realList = await _dcbs.GetPullListAsync(ct);
        var match = realList.FirstOrDefault(r => TitleNormalizer.Normalize(r.Title) == entry.NormalizedTitle);
        if (match is null)
        {
            return false;
        }

        entry.DcbsPullListId = match.PullListId;
        entry.LastVerifiedStickyAt = DateTime.UtcNow;
        return true;
    }

    private async Task<(string ProductCode, string OrderId)?> FindKnownOrRecentPurchaseAsync(
        PullListEntry entry, string normalizedTitle, CancellationToken ct)
    {
        if (entry.LastKnownProductCode is not null && entry.LastKnownOrderId is not null)
        {
            return (entry.LastKnownProductCode, entry.LastKnownOrderId);
        }

        var orderIds = await _dcbs.GetRecentOrderIdsAsync(RecentOrdersToScan, ct);
        foreach (var orderId in orderIds)
        {
            var lines = await _dcbs.GetOrderLinesAsync(orderId, ct);
            var match = lines.FirstOrDefault(l => TitleNormalizer.Normalize(l.Title).Contains(normalizedTitle)
                || normalizedTitle.Contains(TitleNormalizer.Normalize(l.Title)));
            if (match is not null)
            {
                return (match.ProductCode, orderId);
            }
        }

        return null;
    }

    private static string Truncate(string value, int max = 2000) =>
        value.Length <= max ? value : value[..max];
}
