using System.Text.RegularExpressions;
using ComicReliefCoreApi.Api.Services;

namespace ComicReliefCoreApi.App.Services;

/// <summary>Series + issue identity of a cart/order/solicitation title, shared by Cart Check and the Candidates tab's in-cart marks.</summary>
public static class CartItemIdentity
{
    // DCBS tags some volumes with their launch year - "Vampirella (2026) #10" - but not
    // consistently: the same 2026 run also appears as plain "Vampirella #6". So the year is
    // dropped from the series name; telling volumes apart is left to the caller.
    private static readonly Regex TrailingYearRegex = new(@" (19|20)\d{2}$", RegexOptions.Compiled);

    /// <summary>The series name (everything before "#&lt;number&gt;"), normalized with word boundaries kept and any trailing year dropped.</summary>
    public static string SeriesKey(string title)
    {
        var series = TitleNormalizer.NormalizeKeepingWordBoundaries(IssueNumberParser.TryExtractSeriesTitle(title) ?? title);
        return TrailingYearRegex.Replace(series, "");
    }

    /// <summary>
    /// Same series name (exactly, apart from a year tag) and same "#&lt;number&gt;". Exact on
    /// both because looser matching paired "Superman" with "Batman Superman Worlds Finest",
    /// "Vampirella" with "Vampirella Paradise", and "#10" with "#1005" (all confirmed live).
    /// </summary>
    public static bool IsSameSeriesAndIssue(string a, string b)
    {
        var issueA = IssueNumberParser.TryParseWholeIssueNumber(a);
        return issueA is not null
            && issueA == IssueNumberParser.TryParseWholeIssueNumber(b)
            && SeriesKey(a) == SeriesKey(b);
    }
}
