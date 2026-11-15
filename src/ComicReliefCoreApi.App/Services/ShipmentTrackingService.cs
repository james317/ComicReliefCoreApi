using System.Text.RegularExpressions;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Models.Dcbs;
using ComicReliefCoreApi.Api.Services;
using ComicReliefCoreApi.Api.Services.Dcbs;

namespace ComicReliefCoreApi.App.Services;

public class ShipmentTrackingService : IShipmentTrackingService
{
    // CLZ's own "Series" field routinely carries decoration DCBS's titles never do -
    // ", Vol. N" (e.g. "Batman, Vol. 4", "Vampirella, Vol. 8 (Dynamite)") and a trailing
    // publisher/imprint disambiguator in parens (e.g. "Cult-de-Sac (Ignition Press)",
    // "The Eye Collector (Image Comics)") - confirmed against the user's real export.
    // Stripped before matching so IsLikelySeriesMatch (built for DCBS's own naming quirks)
    // has a fair comparison; parens first since a couple of rows carry both ("Vampirella,
    // Vol. 8 (Dynamite)" needs the paren gone before the volume-comma pattern applies cleanly).
    private static readonly Regex ClzTrailingParenRegex = new(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);
    private static readonly Regex ClzTrailingVolumeRegex = new(@",?\s*Vol\.?\s*\d+\s*$", RegexOptions.Compiled);

    private readonly IDcbsClient _dcbs;
    private readonly IClzCollectionService _clz;

    public ShipmentTrackingService(IDcbsClient dcbs, IClzCollectionService clz)
    {
        _dcbs = dcbs;
        _clz = clz;
    }

    public Task<IReadOnlyList<DcbsShipmentSummary>> GetRecentShipmentsAsync(int max = 12, CancellationToken ct = default) =>
        _dcbs.GetRecentShipmentsAsync(max, ct);

    public async Task<ShipmentReadingOrder> GetReadingOrderAsync(
        string shipmentId, DateOnly? shippedAt = null, CancellationToken ct = default)
    {
        var lines = await _dcbs.GetShipmentLinesAsync(shipmentId, ct);
        var clzIssues = await _clz.GetAllIssueReleasesAsync(ct);
        var shipDate = shippedAt ?? await LookUpShipDateAsync(shipmentId, ct);

        var items = lines.Select(line =>
        {
            var issueNumber = IssueNumberParser.TryParseWholeIssueNumber(line.Title);
            // maxGapWords: 2 - safe here specifically because the exact issue number is
            // already checked independently (c.IssueNumber == issueNumber) rather than just
            // "some digit follows," and a wrong match only affects a display grouping, not
            // which series something gets tracked as (see TitleNormalizer's own docs on why
            // pull-list/solicitation matching can't afford this same width).
            var candidates = issueNumber is null
                ? new List<ClzIssueRelease>()
                : clzIssues.Where(c => c.IssueNumber == issueNumber && TitleNormalizer.IsLikelySeriesMatch(line.Title, CleanClzSeriesName(c.Series), maxGapWords: 2)).ToList();
            return new ShipmentReadingItem(line.ProductCode, line.Title, PickReleaseDate(MostSpecific(candidates), shipDate));
        }).ToList();

        var groups = items
            .GroupBy(i => i.ReleaseDate)
            .Select(g => new ShipmentReadingGroup(g.Key, g.ToList()))
            // Known dates first in ship order; the "unknown" group (no CLZ match) goes last
            // rather than sorting as if it were the earliest date.
            .OrderBy(g => g.ReleaseDate.HasValue ? 0 : 1)
            .ThenBy(g => g.ReleaseDate)
            .ToList();

        return new ShipmentReadingOrder(shipmentId, groups);
    }

    // CleanClzSeriesName strips ", Vol. N" so CLZ names can match DCBS's undecorated titles -
    // which also means every volume of a long-running title matches: "Batman #14" matches
    // both "Batman, Vol. 3" #14 (2017) and "Batman, Vol. 4" #14 (2026). Confirmed live
    // 11/15/2026: taking the first match put Batman #14/#15, Superman #43, Vampirella #7 and
    // Avengers #1 at 2017-2023 dates at the top of a box shipped 11/10/2026. Nothing can be in
    // a box before it came out, so the right volume is the latest release on or before the
    // ship date; an older volume's same issue number is years earlier, a newer one is after.
    // The slack covers DCBS shipping a few days ahead of CLZ's listed date - confirmed live:
    // Vampirella Summer Special 2026 #1 (7/1/2026) arrived in a box marked shipped 6/30/2026.
    private static readonly int ShipDateSlackDays = 14;

    private static DateOnly? PickReleaseDate(IReadOnlyList<ClzIssueRelease> candidates, DateOnly shipDate)
    {
        var dated = candidates.Where(c => c.ReleaseDate.HasValue).Select(c => c.ReleaseDate!.Value).ToList();
        if (dated.Count == 0)
        {
            return null;
        }
        var cutoff = shipDate.AddDays(ShipDateSlackDays);
        var onOrBefore = dated.Where(d => d <= cutoff).ToList();
        return onOrBefore.Count > 0 ? onOrBefore.Max() : dated.Min();
    }

    // maxGapWords: 2 lets a bare "Vampirella" match "Vampirella Summer Special 2026 #1" (the
    // "2026" counts as the number) alongside the real "Vampirella Summer Special 2026" row -
    // so before the date rule picks between volumes, keep only the longest series names that
    // matched. Volumes of one title clean to the same name ("Batman"), so this never removes
    // the volume choice the date rule exists to make.
    private static IReadOnlyList<ClzIssueRelease> MostSpecific(IReadOnlyList<ClzIssueRelease> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }
        var lengths = candidates.ToDictionary(c => c, c => TitleNormalizer.NormalizeKeepingWordBoundaries(CleanClzSeriesName(c.Series)).Length);
        var longest = lengths.Values.Max();
        return candidates.Where(c => lengths[c] == longest).ToList();
    }

    private async Task<DateOnly> LookUpShipDateAsync(string shipmentId, CancellationToken ct)
    {
        var shipments = await _dcbs.GetRecentShipmentsAsync(50, ct);
        return shipments.FirstOrDefault(s => s.ShipmentId == shipmentId)?.ShippedAt
            ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static string CleanClzSeriesName(string series)
    {
        var withoutParen = ClzTrailingParenRegex.Replace(series, "").TrimEnd();
        return ClzTrailingVolumeRegex.Replace(withoutParen, "").TrimEnd();
    }
}
