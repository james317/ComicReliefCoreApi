using System.Text.Json.Serialization;

namespace ComicReliefCoreApi.Models;

// Raw shapes returned by the Comic Vine /issues/ endpoint.
// See https://comicvine.gamespot.com/api/documentation

internal sealed class ComicVineIssuesResponse
{
    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("number_of_total_results")]
    public int NumberOfTotalResults { get; set; }

    [JsonPropertyName("results")]
    public List<ComicVineIssueDto> Results { get; set; } = new();
}

internal sealed class ComicVineIssueDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("issue_number")]
    public string? IssueNumber { get; set; }

    [JsonPropertyName("store_date")]
    public string? StoreDate { get; set; }

    [JsonPropertyName("cover_date")]
    public string? CoverDate { get; set; }

    [JsonPropertyName("volume")]
    public ComicVineVolumeRefDto? Volume { get; set; }

    [JsonPropertyName("image")]
    public ComicVineImageDto? Image { get; set; }

    [JsonPropertyName("site_detail_url")]
    public string? SiteDetailUrl { get; set; }
}

internal sealed class ComicVineVolumeRefDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class ComicVineImageDto
{
    [JsonPropertyName("medium_url")]
    public string? MediumUrl { get; set; }

    [JsonPropertyName("small_url")]
    public string? SmallUrl { get; set; }
}
