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
    private static readonly Regex NonAlphanumericKeepSpace = new("[^a-z0-9 ]", RegexOptions.Compiled);
    private static readonly Regex CollapseSpaces = new("\\s+", RegexOptions.Compiled);

    public static string Normalize(string title)
    {
        var lowered = title.Trim().ToLowerInvariant();
        var withoutArticle = LeadingArticle.Replace(lowered, "");
        return NonAlphanumeric.Replace(withoutArticle, "");
    }

    private static string NormalizeKeepingWordBoundaries(string title)
    {
        var lowered = title.Trim().ToLowerInvariant();
        var withoutArticle = LeadingArticle.Replace(lowered, "");
        var alphanumericAndSpaces = NonAlphanumericKeepSpace.Replace(withoutArticle, " ");
        return CollapseSpaces.Replace(alphanumericAndSpaces, " ").Trim();
    }

    /// <summary>
    /// True if listingTitle (e.g. a DCBS solicitation's full title, issue number and variant
    /// info included, like "Batman #25 Cvr F Jonboy Meyers...") is plausibly an issue of
    /// seriesTitle. Normalize() alone can't answer this: it strips every space, so "Batman"
    /// becomes a character-prefix of both a real Batman issue AND an unrelated series like
    /// "Batman/Superman" ("batmansuperman..." also starts with "batman"). This keeps word
    /// boundaries and additionally requires the token right after the series name to be
    /// numeric (a real issue number) - guarding against exactly the false-positive class this
    /// session already hit once with CLZ matching ("Archie Meets Batman 66" vs a bare
    /// "Batman" pull-list entry). Trade-off: one-shots/TPBs/annuals with no issue number right
    /// after the series name (e.g. "Batman: Killing Joke Deluxe Ed HC") won't match - accepted
    /// as a false negative rather than risk a false positive.
    /// </summary>
    public static bool IsLikelySeriesMatch(string listingTitle, string seriesTitle)
    {
        var listing = NormalizeKeepingWordBoundaries(listingTitle);
        var series = NormalizeKeepingWordBoundaries(seriesTitle);
        if (series.Length == 0)
        {
            return false;
        }
        if (listing == series)
        {
            return true;
        }
        if (!listing.StartsWith(series + " ", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = listing[(series.Length + 1)..];
        return rest.Length > 0 && char.IsDigit(rest[0]);
    }
}
