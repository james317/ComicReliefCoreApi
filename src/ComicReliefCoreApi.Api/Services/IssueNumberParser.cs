using System.Text.RegularExpressions;

namespace ComicReliefCoreApi.Api.Services;

/// <summary>
/// Pulls the whole-number issue number out of a DCBS listing/order-line title (e.g. "Batman
/// #12 Cvr A Jorge Jimenez" -&gt; 12). Deliberately only recognizes plain integers: decimal
/// issues ("Batman #12.5") are one-off interludes that aren't part of a series' normal
/// numbering sequence, so they're left unparsed rather than truncated to a misleading whole
/// number that would look like a duplicate or an out-of-order issue.
/// </summary>
public static class IssueNumberParser
{
    private static readonly Regex WholeIssueNumberRegex = new(@"#(\d+)(?!\.\d)\b", RegexOptions.Compiled);

    public static int? TryParseWholeIssueNumber(string title)
    {
        var match = WholeIssueNumberRegex.Match(title);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }
}
