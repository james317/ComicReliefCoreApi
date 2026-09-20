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
    private static readonly Regex Apostrophe = new("['’]", RegexOptions.Compiled);
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
        // Apostrophes are removed (not turned into a space) before the general punctuation
        // pass, specifically to reconcile a real mismatch confirmed live: CLZ's "World's
        // Finest" keeps the possessive letter ("world's" -> naively "world s", two tokens),
        // while DCBS's own title drops it entirely ("Worlds Finest"). Removing the apostrophe
        // outright merges "world's" into "worlds" too, matching DCBS's own convention, rather
        // than leaving them as two differently-tokenized words that can never line up.
        var withoutApostrophes = Apostrophe.Replace(withoutArticle, "");
        var alphanumericAndSpaces = NonAlphanumericKeepSpace.Replace(withoutApostrophes, " ");
        return CollapseSpaces.Replace(alphanumericAndSpaces, " ").Trim();
    }

    /// <summary>
    /// True if listingTitle (e.g. a DCBS solicitation's full title, issue number and variant
    /// info included, like "Batman #25 Cvr F Jonboy Meyers...") is plausibly an issue of
    /// seriesTitle. Normalize() alone can't answer this: it strips every space, so "Batman"
    /// becomes a character-prefix of both a real Batman issue AND an unrelated series like
    /// "Batman/Superman" ("batmansuperman..." also starts with "batman"). This keeps word
    /// boundaries and additionally requires the token right after the series name (allowing
    /// up to maxGapWords words in between - see below) to be numeric (a real issue number) -
    /// guarding against exactly the false-positive class this session already hit once with
    /// CLZ matching ("Archie Meets Batman 66" vs a bare "Batman" pull-list entry). Trade-off:
    /// one-shots/TPBs/annuals with no issue number right after the series name (e.g. "Batman:
    /// Killing Joke Deluxe Ed HC") won't match - accepted as a false negative rather than risk
    /// a false positive.
    /// </summary>
    /// <param name="maxGapWords">
    /// Default 0 preserves the original strict "issue number immediately follows the series
    /// name" rule - keep this for any caller matching against an open-ended universe of
    /// titles (pull-list/solicitation matching), where a false positive silently attaches the
    /// wrong series. A caller that also independently verifies the exact issue number against
    /// a specific known value (not just "some digit") - as the shipment-reading-order feature
    /// does against a specific CLZ row - can afford to widen this: confirmed live this session
    /// that some real titles carry a subtitle between series and issue number DCBS solicits
    /// but the CLZ series name doesn't record ("X-Men '97 Season Two #3", "Madame Tarantula
    /// Magazine #2") - both false negatives under the strict rule, both fixed at maxGapWords=2.
    /// </param>
    /// <summary>
    /// True if one of these titles' normalized forms is a truncated prefix of the other,
    /// off by only a couple of characters - DCBS is known to hard-truncate a series name
    /// in some contexts (a DB column limit on its own persistent /account/pulllist page,
    /// confirmed live) while spelling it out in full elsewhere (order-line/product
    /// titles), which otherwise registers the same real series as two different tracked
    /// entries - confirmed live 9/2026: "Pathfinder / Vampirella: Blade of Darknes"
    /// (pull-list page, truncated) vs "Pathfinder Vampirella Blade of Darkness" (order
    /// line, in full) created separate Sticky and Unsticky rows for one real series.
    /// Deliberately narrow - a couple of characters, not a general substring/prefix check
    /// - a wide version would wrongly merge genuinely distinct titles like "Batman" and
    /// "Batman Beyond" (already normalized inputs expected, e.g. via Normalize()).
    /// </summary>
    public static bool IsLikelyTruncatedVariant(string normalizedA, string normalizedB, int maxTruncation = 2)
    {
        var (shorter, longer) = normalizedA.Length <= normalizedB.Length ? (normalizedA, normalizedB) : (normalizedB, normalizedA);
        if (shorter.Length == 0)
        {
            return false;
        }
        return longer.StartsWith(shorter, StringComparison.Ordinal) && longer.Length - shorter.Length <= maxTruncation;
    }

    public static bool IsLikelySeriesMatch(string listingTitle, string seriesTitle, int maxGapWords = 0)
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
        var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordsToCheck = Math.Min(words.Length, maxGapWords + 1);
        for (var i = 0; i < wordsToCheck; i++)
        {
            if (words[i].Length > 0 && char.IsDigit(words[i][0]))
            {
                return true;
            }
        }
        return false;
    }
}
