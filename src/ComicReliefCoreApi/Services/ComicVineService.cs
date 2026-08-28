using System.Globalization;
using System.Net.Http.Json;
using ComicReliefCoreApi.Configuration;
using ComicReliefCoreApi.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ComicReliefCoreApi.Services;

/// <summary>Fetches issues shipping in a given date range from the Comic Vine API.</summary>
public sealed class ComicVineService : IComicVineService
{
    private const string IssuesUrl = "https://comicvine.gamespot.com/api/issues/";
    private const int PageSize = 100;
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly ComicVineOptions _options;
    private readonly ILogger<ComicVineService> _logger;

    public ComicVineService(HttpClient httpClient, IOptions<ComicVineOptions> options, ILogger<ComicVineService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Comic Vine rejects requests without a User-Agent.
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ComicReliefCoreApi/1.0");
        }
    }

    public async Task<UpcomingComicsResponse> GetIssuesShippingInAsync(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Comic Vine API key is not configured. Set ComicVine:ApiKey (see README.md).");
        }

        var comics = new List<ComicIssue>();
        var offset = 0;
        var totalResults = int.MaxValue;
        var truncated = false;
        var page = 0;

        while (offset < totalResults)
        {
            page++;
            if (page > Math.Max(1, _options.MaxPages))
            {
                truncated = true;
                break;
            }

            var payload = await FetchPageAsync(rangeStart, rangeEnd, offset, cancellationToken);
            totalResults = payload.NumberOfTotalResults;

            foreach (var dto in payload.Results)
            {
                comics.Add(new ComicIssue(
                    dto.Id,
                    BuildTitle(dto),
                    dto.IssueNumber,
                    ParseDate(dto.StoreDate),
                    dto.Image?.MediumUrl ?? dto.Image?.SmallUrl,
                    dto.SiteDetailUrl));
            }

            offset += PageSize;

            if (offset < totalResults && page < _options.MaxPages)
            {
                await Task.Delay(RateLimitDelay, cancellationToken);
            }
        }

        comics.Sort((a, b) => Nullable.Compare(a.StoreDate, b.StoreDate));

        return new UpcomingComicsResponse(
            rangeStart.Year,
            rangeStart.Month,
            rangeStart.ToString("MMMM", CultureInfo.InvariantCulture),
            rangeStart,
            rangeEnd,
            comics.Count,
            truncated,
            comics);
    }

    private async Task<ComicVineIssuesResponse> FetchPageAsync(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        int offset,
        CancellationToken cancellationToken)
    {
        var filter = $"store_date:{rangeStart:yyyy-MM-dd}|{rangeEnd:yyyy-MM-dd}";
        var query = new Dictionary<string, string?>
        {
            ["api_key"] = _options.ApiKey,
            ["format"] = "json",
            ["filter"] = filter,
            ["sort"] = "store_date:asc",
            ["limit"] = PageSize.ToString(CultureInfo.InvariantCulture),
            ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
            ["field_list"] = "id,name,issue_number,store_date,cover_date,volume,image,site_detail_url",
        };
        var url = QueryHelpers.AddQueryString(IssuesUrl, query);

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // Comic Vine returns a structured JSON body describing the failure (e.g. "Invalid
        // API Key") even on non-2xx responses, so try to parse it before falling back to
        // the bare HTTP status.
        ComicVineIssuesResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<ComicVineIssuesResponse>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            _logger.LogWarning("Comic Vine request failed with {StatusCode}", response.StatusCode);
            throw new InvalidOperationException($"Comic Vine API request failed with status {(int)response.StatusCode}.");
        }

        if (payload.StatusCode != 1)
        {
            throw new InvalidOperationException($"Comic Vine API error: {payload.Error ?? "unknown error"}");
        }

        return payload;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var datePart = value.Split(' ')[0];
        return DateOnly.TryParse(datePart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static string BuildTitle(ComicVineIssueDto dto)
    {
        var volumeName = dto.Volume?.Name ?? "Unknown Series";
        var issueSuffix = string.IsNullOrWhiteSpace(dto.IssueNumber) ? string.Empty : $" #{dto.IssueNumber}";

        var hasDistinctName = !string.IsNullOrWhiteSpace(dto.Name)
            && !string.Equals(dto.Name, dto.IssueNumber, StringComparison.OrdinalIgnoreCase);

        return hasDistinctName
            ? $"{volumeName}{issueSuffix} — {dto.Name}"
            : $"{volumeName}{issueSuffix}";
    }
}
