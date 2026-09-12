using System.Net;
using System.Text.RegularExpressions;
using ComicReliefCoreApi.Api.Configuration;
using ComicReliefCoreApi.Api.Models.Dcbs;
using Microsoft.Extensions.Options;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

public class DcbsClient : IDcbsClient
{
    private readonly HttpClient _http;
    private readonly DcbsOptions _options;
    private readonly IDcbsSessionStore _sessionStore;

    private static readonly Regex SearchResultRowRegex = new(
        "<div class=\"seriescode\">([^<]+)</div>\\s*</td>\\s*<td><div class=\"seriestitle\">([^<]+)</div></td>\\s*<td>([^<]*)</td>",
        RegexOptions.Compiled);

    private static readonly Regex PullListTitleRegex = new("seriestitle\">([^<]+)</span>", RegexOptions.Compiled);
    private static readonly Regex PullListQtyRegex = new("name=\"qty\" type=\"text\" value=\"(\\d+)\"", RegexOptions.Compiled);
    private static readonly Regex PullListPlidRegex = new("name=\"id\" type=\"hidden\" value=\"(\\d+)\"", RegexOptions.Compiled);

    // Order detail page parsing - chunked per <tr> rather than three independent flat
    // regex-and-zip lists (the original approach here, and still how the publisher listing
    // page works). Confirmed live this session that flat lists disagree in count: the same
    // order page had 40 titles, 40 visible product-code divs, but only 38 hidden
    // productcode inputs and 38 status icons - the two free/no-status rows (Comic Shop
    // News, monthly catalogs) don't get a pull-list-add form or a status icon at all, so a
    // positional zip across differently-sized lists would silently misalign every row after
    // the first missing one. Splitting into row chunks first and pulling each field from
    // its own chunk sidesteps that entirely - a field just comes back null/skipped for that
    // one row instead of shifting every later row.
    //
    // Known gap, confirmed on the same real order: this non-greedy "<tr>(.*?)</tr>" can't
    // see a row whose own markup embeds another "<tr>...</tr>" pair before its real close (2
    // of 40 rows on that order) - it silently drops the whole row rather than truncating it
    // into a wrong one, so nothing gets misattributed, but those 2 items are missing
    // entirely rather than appearing with a null status. Accepted for now since both
    // instances seen so far were free items (a monthly catalog, Comic Shop News) that
    // wouldn't match any real pull-list series anyway - a real HTML parser would be the
    // actual fix if a real series title ever turns out to hit this.
    private static readonly Regex OrderRowRegex = new("<tr>(.*?)</tr>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex OrderRowProductCodeRegex = new(
        "<div class=\"productcode\">\\s*([^<]+?)\\s*</div>", RegexOptions.Compiled);
    private static readonly Regex OrderCartImgAltRegex = new(
        "class=\"cartimg\" alt=\"([^\"]+)\"|alt=\"([^\"]+)\" class=\"cartimg\"",
        RegexOptions.Compiled);
    // Title-case only ("Processing.png" etc.) - deliberately excludes the Shipment Status
    // Legend's own explanatory icons, which use the same filenames all-lowercase
    // ("processing.png"). Confirmed live: every real item row is title-case, the legend
    // block (always after every real row) is always lowercase - no order-of-appearance
    // assumption needed.
    private static readonly Regex OrderRowStatusRegex = new(
        "alt='(Processing|Filled|Shipped|Cancelled)' title='[^']*' border='0' class='statusimg'",
        RegexOptions.Compiled);

    private static readonly Regex OrderIdLinkRegex = new("href=\"/account/order/(\\d+)\"", RegexOptions.Compiled);

    // /account/shipments row: shipment id, the packlist number printed inside the real box
    // (the only thing the user can see without a browser), and the ship date. Confirmed
    // live against every row on this account's real shipment history.
    private static readonly Regex ShipmentRowRegex = new(
        "<a href=\"/account/shipment/(\\d+)\">\\d+</a></td>\\s*<td class=\"compactoff\"><a href=\"/account/shipment/\\d+\">([^<]+)</a></td>\\s*<td>([\\d/]+)</td>",
        RegexOptions.Compiled);

    // A shipment detail page's own row layout differs from an order's: the real title sits
    // as plain text right before the productcode div (order pages instead carry it in the
    // cartimg alt, which on a shipment page holds the long creators/description blurb
    // instead - confirmed live, reusing OrderCartImgAltRegex here would silently grab the
    // wrong text).
    private static readonly Regex ShipmentRowTitleRegex = new(
        ">([^<]+?)<br\\s*/>\\s*<div class=\"productcode\">", RegexOptions.Compiled);

    // Publisher listing-page parsing - the real results grid lives inside
    // <ul class="thumblist">, same container class documented for /search pages (see
    // docs/BACKLOG.md). Scoping to that container first, then splitting on "<li " to get
    // one chunk per product, avoids accidentally matching nav-menu <li> elements that
    // appear earlier in the page.
    private static readonly Regex ThumbListRegex = new(
        "<ul class=\"thumblist\">([\\s\\S]*?)</ul>", RegexOptions.Compiled);
    private static readonly Regex ListingProductLinkRegex = new(
        "<a href=\"(/product/([^/\"]+)/[^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex ListingTitleRegex = new(
        "<h5><a href=\"[^\"]+\">([^<]+)</a></h5>", RegexOptions.Compiled);
    private static readonly Regex ListingDescriptionRegex = new(
        "</h5>\\s*<div>([\\s\\S]*?)</div>", RegexOptions.Compiled);
    private static readonly Regex ListingThumbnailRegex = new(
        "class=\"thumbnail\" src=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex ListingPriceRegex = new(
        "DCBS Price: </span>\\$([\\d.]+)", RegexOptions.Compiled);

    // Second reprint marker found live 9/2026, alongside "Facsimile Edition": DCBS also
    // marks additional print runs of a still-selling issue as "2nd Ptg"/"3rd Ptg"/etc
    // ("printing") - same problem (this isn't the current volume's new issue) with
    // different wording. Caught via a real case: Crowbound #1 "2nd Ptg" was showing up as
    // a pull-list match alongside the genuinely new Crowbound #2 that same month.
    private static readonly Regex ReprintMarkerRegex = new(
        "facsimile edition|\\bptg\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DcbsClient(HttpClient http, IOptions<DcbsOptions> options, IDcbsSessionStore sessionStore)
    {
        _options = options.Value;
        _sessionStore = sessionStore;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15");
    }

    /// <summary>
    /// Attaches the current session cookie to a request, reading it fresh from the
    /// database each time rather than fixing it once at startup - the whole point of
    /// this indirection is that a new cookie can be pasted in at runtime (see
    /// DcbsSessionController) without needing to restart the app to pick it up.
    /// </summary>
    private async Task PrepareRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var cookie = await _sessionStore.GetCookieAsync(ct);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            // TryAddWithoutValidation, not Add: a real captured cookie string contains
            // URL-encoded values (e.g. the cookie-consent blob) that can trip .NET's
            // strict header validation despite being perfectly valid on the wire.
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await PrepareRequestAsync(request, ct);
        return await _http.SendAsync(request, ct);
    }

    public async Task<(int StatusCode, string Body)> GetRawAsync(
        string relativeUrl, IReadOnlyDictionary<string, string>? extraCookies = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        var cookie = await _sessionStore.GetCookieAsync(ct);
        var combined = cookie ?? "";
        if (extraCookies is not null)
        {
            foreach (var (name, value) in extraCookies)
            {
                combined += (combined.Length > 0 ? "; " : "") + $"{name}={value}";
            }
        }
        if (combined.Length > 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie", combined);
        }

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return ((int)response.StatusCode, body);
    }

    public async Task<IReadOnlyList<DcbsListingItem>> GetPublisherListingAsync(
        string categorySlug, int categoryId, CancellationToken ct = default)
    {
        // 1000 comfortably exceeds every publisher's current inventory observed while
        // testing this (DC's 325 was the largest) - confirmed live that DCBS returns
        // everything in one page rather than capping at the UI dropdown's 100 max.
        var extraCookies = new Dictionary<string, string> { ["ProductsPerPage"] = "1000" };
        var (statusCode, body) = await GetRawAsync($"/products/{categorySlug}/{categoryId}", extraCookies, ct);
        if (statusCode != 200)
        {
            return Array.Empty<DcbsListingItem>();
        }

        var thumbListMatch = ThumbListRegex.Match(body);
        if (!thumbListMatch.Success)
        {
            return Array.Empty<DcbsListingItem>();
        }

        var items = new List<DcbsListingItem>();
        var chunks = thumbListMatch.Groups[1].Value.Split("<li ", StringSplitOptions.RemoveEmptyEntries);
        foreach (var chunk in chunks)
        {
            var linkMatch = ListingProductLinkRegex.Match(chunk);
            var titleMatch = ListingTitleRegex.Match(chunk);
            if (!linkMatch.Success || !titleMatch.Success)
            {
                continue;
            }

            var descriptionMatch = ListingDescriptionRegex.Match(chunk);
            var thumbnailMatch = ListingThumbnailRegex.Match(chunk);
            var priceMatch = ListingPriceRegex.Match(chunk);
            var price = priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, out var parsedPrice)
                ? parsedPrice
                : (decimal?)null;
            var title = WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());

            items.Add(new DcbsListingItem(
                ProductCode: linkMatch.Groups[2].Value,
                Title: title,
                ProductUrl: _options.BaseUrl + linkMatch.Groups[1].Value,
                ThumbnailUrl: thumbnailMatch.Success ? thumbnailMatch.Groups[1].Value : null,
                CreatorsAndDescription: descriptionMatch.Success
                    ? WebUtility.HtmlDecode(descriptionMatch.Groups[1].Value.Trim())
                    : null,
                Price: price,
                // DCBS marks these with class="relist" on the <li> itself, right at the
                // start of the chunk since we split on "<li " - a plain substring check on
                // the whole chunk would also match if that text ever showed up inside a
                // solicitation blurb, so this is scoped to just the opening tag.
                IsRelisted: chunk.TrimStart().StartsWith("class=relist", StringComparison.OrdinalIgnoreCase),
                // Checked live (9/2026): DCBS exposes no volume/series-generation field
                // anywhere, on the listing or product page. "Facsimile Edition" and "Nth
                // Ptg" in the title itself are the only markers it gives for "this isn't
                // the current volume's new issue" - see ReprintMarkerRegex.
                IsFacsimileOrReprint: ReprintMarkerRegex.IsMatch(title)));
        }
        return items;
    }

    public async Task<IReadOnlyList<DcbsSeriesSearchResult>> SearchSeriesAsync(string term, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["search"] = term });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ajax/PullListSearch") { Content = content };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Referrer = new Uri(_options.BaseUrl + "/account/pulllist");
        await PrepareRequestAsync(request, ct);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<DcbsSeriesSearchResult>();
        }

        var results = new List<DcbsSeriesSearchResult>();
        foreach (Match m in SearchResultRowRegex.Matches(body))
        {
            var code = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            var title = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
            var currentIssue = WebUtility.HtmlDecode(m.Groups[3].Value.Trim());
            results.Add(new DcbsSeriesSearchResult(code, title, string.IsNullOrEmpty(currentIssue) ? null : currentIssue));
        }
        return results;
    }

    public async Task<(bool Success, string RawResponse)> TryAddPullListItemAsync(
        string seriesCode, string title, int qty = 1, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["seriesCode"] = seriesCode,
            ["qtyToAdd"] = qty.ToString(),
            ["title"] = title,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ajax/AddPullListItem") { Content = content };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Referrer = new Uri(_options.BaseUrl + "/account/pulllist");
        await PrepareRequestAsync(request, ct);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // A raw 500 (not a graceful {"success":false,...}) is a known live DCBS bug for
        // long series codes - report it plainly rather than throwing, so callers can
        // fall back to the order-form route.
        if (!response.IsSuccessStatusCode)
        {
            return (false, $"HTTP {(int)response.StatusCode}");
        }

        var success = body.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase);
        return (success, body);
    }

    public async Task<IReadOnlyList<DcbsPullListRow>> GetPullListAsync(CancellationToken ct = default)
    {
        using var response = await GetAsync("/account/pulllist", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var rows = new List<DcbsPullListRow>();

        foreach (var chunk in body.Split("<tr>"))
        {
            var titleMatch = PullListTitleRegex.Match(chunk);
            var qtyMatch = PullListQtyRegex.Match(chunk);
            var plidMatch = PullListPlidRegex.Match(chunk);
            if (titleMatch.Success && qtyMatch.Success && plidMatch.Success)
            {
                rows.Add(new DcbsPullListRow(
                    WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim()),
                    int.Parse(qtyMatch.Groups[1].Value),
                    plidMatch.Groups[1].Value));
            }
        }
        return rows;
    }

    public async Task<IReadOnlyList<DcbsOrderLine>> GetOrderLinesAsync(string orderId, CancellationToken ct = default)
    {
        using var response = await GetAsync($"/account/order/{orderId}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        var lines = new List<DcbsOrderLine>();
        foreach (Match row in OrderRowRegex.Matches(body))
        {
            var chunk = row.Groups[1].Value;
            var codeMatch = OrderRowProductCodeRegex.Match(chunk);
            if (!codeMatch.Success)
            {
                continue; // header row, or a row with no product (neither seen live, but skip rather than guess)
            }

            var titleMatch = OrderCartImgAltRegex.Match(chunk);
            var title = titleMatch.Success
                ? WebUtility.HtmlDecode(titleMatch.Groups[1].Success ? titleMatch.Groups[1].Value : titleMatch.Groups[2].Value)
                : codeMatch.Groups[1].Value;

            var statusMatch = OrderRowStatusRegex.Match(chunk);
            DcbsShipmentStatus? status = statusMatch.Success
                ? Enum.Parse<DcbsShipmentStatus>(statusMatch.Groups[1].Value)
                : null;

            lines.Add(new DcbsOrderLine(codeMatch.Groups[1].Value, title, status));
        }
        return lines;
    }

    public async Task<IReadOnlyList<DcbsShipmentSummary>> GetRecentShipmentsAsync(int max = 12, CancellationToken ct = default)
    {
        using var response = await GetAsync("/account/shipments", ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        var shipments = new List<DcbsShipmentSummary>();
        foreach (Match m in ShipmentRowRegex.Matches(body))
        {
            if (DateOnly.TryParse(m.Groups[3].Value, out var shippedAt))
            {
                shipments.Add(new DcbsShipmentSummary(m.Groups[1].Value, m.Groups[2].Value, shippedAt));
            }
        }
        return shipments.Take(max).ToList();
    }

    public async Task<IReadOnlyList<DcbsShipmentLine>> GetShipmentLinesAsync(string shipmentId, CancellationToken ct = default)
    {
        using var response = await GetAsync($"/account/shipment/{shipmentId}", ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        var lines = new List<DcbsShipmentLine>();
        foreach (Match row in OrderRowRegex.Matches(body))
        {
            var chunk = row.Groups[1].Value;
            var codeMatch = OrderRowProductCodeRegex.Match(chunk);
            if (!codeMatch.Success)
            {
                continue;
            }

            var titleMatch = ShipmentRowTitleRegex.Match(chunk);
            var title = titleMatch.Success ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim()) : codeMatch.Groups[1].Value;

            lines.Add(new DcbsShipmentLine(codeMatch.Groups[1].Value, title));
        }

        // Same nested-<tr> limitation as the order parser, manifesting differently here:
        // confirmed live on a real shipment that one row's own alt-text content (a long
        // solicitation blurb) can garble the non-greedy match enough to spuriously re-match
        // a later row's product code with mangled title text, producing a duplicate line for
        // that code. Keeping the first occurrence (the well-formed one, seen before any
        // garbling) rather than the garbled duplicate.
        return lines.GroupBy(l => l.ProductCode).Select(g => g.First()).ToList();
    }

    public async Task<bool> TryUpdatePullListFromOrderAsync(
        string orderId,
        IReadOnlyList<string> allProductCodesInOrder,
        string targetProductCode,
        int qty = 1,
        CancellationToken ct = default)
    {
        // DCBS pairs pulllistqty/productcode fields positionally on this form - every
        // row from the order must be resubmitted, not just the target one, or the
        // pairing breaks. Untouched rows go through at qty 0, which is safe: this was
        // verified this session to be a no-op rather than a removal.
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var code in allProductCodesInOrder)
        {
            var thisQty = string.Equals(code, targetProductCode, StringComparison.OrdinalIgnoreCase) ? qty : 0;
            pairs.Add(new KeyValuePair<string, string>("pulllistqty", thisQty.ToString()));
            pairs.Add(new KeyValuePair<string, string>("productcode", code));
        }

        using var content = new FormUrlEncodedContent(pairs);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Account/UpdatePullListFromOrder/{orderId}")
        {
            Content = content,
        };
        request.Headers.Referrer = new Uri($"{_options.BaseUrl}/account/order/{orderId}");
        await PrepareRequestAsync(request, ct);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        // This endpoint gives no reliable success/failure signal of its own (confirmed
        // this session - it just redirects back to the order page either way). The
        // caller is responsible for verifying via GetPullListAsync afterward.
        return true;
    }

    public async Task<IReadOnlyList<string>> GetRecentOrderIdsAsync(int max = 6, CancellationToken ct = default)
    {
        using var response = await GetAsync("/account/orders", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return OrderIdLinkRegex.Matches(body)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Take(max)
            .ToList();
    }

    public async Task<bool> IsSessionValidAsync(CancellationToken ct = default)
    {
        using var response = await GetAsync("/account/pulllist", ct);
        // DCBS redirects an unauthenticated/expired session to its login page rather
        // than returning an error status - the real signal is where we ended up, not
        // the HTTP status code (confirmed this session; see docs/BACKLOG.md).
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
        if (finalUrl.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode && body.Contains("seriestitle", StringComparison.OrdinalIgnoreCase);
    }
}
