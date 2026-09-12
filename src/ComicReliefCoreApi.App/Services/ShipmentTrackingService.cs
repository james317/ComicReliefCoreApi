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

    public async Task<ShipmentReadingOrder> GetReadingOrderAsync(string shipmentId, CancellationToken ct = default)
    {
        var lines = await _dcbs.GetShipmentLinesAsync(shipmentId, ct);
        var clzIssues = await _clz.GetAllIssueReleasesAsync(ct);

        var items = lines.Select(line =>
        {
            var issueNumber = IssueNumberParser.TryParseWholeIssueNumber(line.Title);
            var match = issueNumber is null
                ? null
                : clzIssues.FirstOrDefault(c => c.IssueNumber == issueNumber && TitleNormalizer.IsLikelySeriesMatch(line.Title, CleanClzSeriesName(c.Series)));
            return new ShipmentReadingItem(line.ProductCode, line.Title, match?.ReleaseDate);
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

    private static string CleanClzSeriesName(string series)
    {
        var withoutParen = ClzTrailingParenRegex.Replace(series, "").TrimEnd();
        return ClzTrailingVolumeRegex.Replace(withoutParen, "").TrimEnd();
    }
}
