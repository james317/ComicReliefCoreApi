namespace ComicReliefCoreApi.Configuration;

/// <summary>
/// Bound from the "ComicVine" configuration section. Get a free key at
/// https://comicvine.gamespot.com/api/ and set it via user-secrets or the
/// ComicVine__ApiKey environment variable rather than committing it.
/// </summary>
public sealed class ComicVineOptions
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Safety cap on how many 100-result pages to fetch for a single month,
    /// since Comic Vine rate-limits to roughly one request per second.
    /// </summary>
    public int MaxPages { get; set; } = 10;
}
