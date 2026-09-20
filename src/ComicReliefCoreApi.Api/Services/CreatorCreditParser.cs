using System.Text.RegularExpressions;

namespace ComicReliefCoreApi.Api.Services;

/// <summary>
/// Pulls writer names out of DCBS's own "(W) X (A) Y (CA) Z" credits line - the same raw text
/// already shown as-is on a solicitation card (see solicitation-cards.js's
/// splitCreatorsAndDescription), just parsed here instead of only displayed. Confirmed formats
/// seen live: "(W) Ron Marz (A) Darryl Banks (CA) Jason Geyer, Alex Saviuk", "(W/A/CA) Aaron
/// Lange" (one person covering every role), "(W) Joe Casey (A/CA) Ashley Wood". Deliberately
/// writer-only - artist/cover-artist credits are out of scope for the favorite/avoid feature.
/// </summary>
public static class CreatorCreditParser
{
    // A role group like "(W)", "(W/A)", "(A/CA)" followed by the name(s) up to the next role
    // group, a line break, or the end of the credits line. The lazy "[^(\r\n]+" stops at
    // whichever of those comes first, so it never runs into the free-text solicitation blurb
    // that follows the credits line.
    private static readonly Regex CreditGroupRegex = new(
        @"\(([A-Za-z/]+)\)\s*([^(\r\n]+)", RegexOptions.Compiled);

    public static IReadOnlyList<string> ExtractWriterNames(string? creatorsAndDescription)
    {
        if (string.IsNullOrWhiteSpace(creatorsAndDescription))
        {
            return Array.Empty<string>();
        }

        // Only the first line (before the blank-line gap to the solicitation blurb) is real
        // structured credit data - a parenthetical inside the blurb itself would otherwise
        // risk being misread as a credit group.
        var creditsLine = Regex.Split(creatorsAndDescription, @"\r?\n\r?\n")[0];

        var names = new List<string>();
        foreach (Match match in CreditGroupRegex.Matches(creditsLine))
        {
            var roles = match.Groups[1].Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (!roles.Any(r => r.Equals("W", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            names.AddRange(match.Groups[2].Value.Trim()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return names;
    }
}
