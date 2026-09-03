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

    private static readonly Regex OrderProductCodeRegex = new("name=\"productcode\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex OrderCartImgAltRegex = new(
        "class=\"cartimg\" alt=\"([^\"]+)\"|alt=\"([^\"]+)\" class=\"cartimg\"",
        RegexOptions.Compiled);

    private static readonly Regex OrderIdLinkRegex = new("href=\"/account/order/(\\d+)\"", RegexOptions.Compiled);

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
                // anywhere, on the listing or product page. "Facsimile Edition" in the
                // title itself is the only marker it gives for "this is a reprint of an
                // old issue, not the current volume's new one."
                IsFacsimileOrReprint: title.Contains("Facsimile Edition", StringComparison.OrdinalIgnoreCase)));
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

        var codes = OrderProductCodeRegex.Matches(body).Select(m => m.Groups[1].Value).ToList();
        var titles = OrderCartImgAltRegex.Matches(body)
            .Select(m => WebUtility.HtmlDecode((m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)))
            .ToList();

        var lines = new List<DcbsOrderLine>();
        for (var i = 0; i < codes.Count; i++)
        {
            // Titles come from the top summary block on top of the per-line pull-list
            // form; the two lists aren't guaranteed the same length on every order
            // layout, so pair defensively rather than assuming a 1:1 zip.
            var title = i < titles.Count ? titles[i] : codes[i];
            lines.Add(new DcbsOrderLine(codes[i], title));
        }
        return lines;
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
