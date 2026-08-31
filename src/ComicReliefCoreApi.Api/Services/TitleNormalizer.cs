using System.Text.RegularExpressions;

namespace ComicReliefCoreApi.Api.Services;

/// <summary>
/// DCBS strips "The", apostrophes, and colons from series titles inconsistently
/// (e.g. "Twilight Zone" not "The Twilight Zone", "X-Men 97" not "X-Men '97") - this
/// normalization has to tolerate that or every comparison against DCBS's own data
/// produces false negatives, as happened repeatedly this session. Lives in .Api (not
/// .App, where it started) because both layers need it now - .Api's CLZ CSV import has
/// to normalize series names the same way .App's PullListService normalizes pull-list
/// titles, and .Api can't depend on .App.
/// </summary>
public static class TitleNormalizer
{
    private static readonly Regex LeadingArticle = new("^(the|a)\\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]", RegexOptions.Compiled);

    public static string Normalize(string title)
    {
        var lowered = title.Trim().ToLowerInvariant();
        var withoutArticle = LeadingArticle.Replace(lowered, "");
        return NonAlphanumeric.Replace(withoutArticle, "");
    }
}
