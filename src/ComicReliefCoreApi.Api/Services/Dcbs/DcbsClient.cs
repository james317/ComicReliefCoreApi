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
