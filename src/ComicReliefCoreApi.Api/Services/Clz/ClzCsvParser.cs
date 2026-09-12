using System.Globalization;
using System.Text.RegularExpressions;
using ComicReliefCoreApi.Api.Models;
using ComicReliefCoreApi.Api.Services;

namespace ComicReliefCoreApi.Api.Services.Clz;

/// <summary>
/// Parses a CLZ (Comic Book Collector) "export comics" CSV - one row per owned issue -
/// and aggregates it to one row per series with the latest release date and issue count.
/// Verified against the user's real exports this session: header includes at least
/// Series, Issue, "Release Date" (format seen: "Jan 17, 2024", plus some year-only rows
/// like "1996" for very old back issues); a fuller export adds Imprint/Artist/Writer/etc,
/// none of which this needs.
/// </summary>
public static class ClzCsvParser
{
    private static readonly string[] DateFormats = { "MMM d, yyyy", "MMM dd, yyyy" };
    private static readonly Regex LeadingIssueNumberRegex = new(@"^(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// One row per owned issue, keeping the per-issue release date ParseAndAggregate throws
    /// away - needed to match a specific shipment line (a specific issue number) to when it
    /// actually came out, not just the series' latest release overall. Rows whose "Issue"
    /// value has no leading digit (a TP/HC/magazine with no issue number, e.g. "TP") are
    /// skipped rather than guessed at, same as IssueNumberParser's own whole-number-only rule.
    /// </summary>
    public static List<ClzIssueRelease> ParsePerIssueRows(TextReader reader, DateTime importedAt)
    {
        var header = ReadRow(reader);
        if (header is null)
        {
            return new List<ClzIssueRelease>();
        }

        var seriesIndex = header.IndexOf("Series");
        var issueIndex = header.IndexOf("Issue");
        var releaseDateIndex = header.IndexOf("Release Date");
        if (seriesIndex < 0 || issueIndex < 0)
        {
            throw new InvalidDataException("CSV is missing a \"Series\" or \"Issue\" column - is this a CLZ comics export?");
        }

        var rows = new List<ClzIssueRelease>();
        List<string>? row;
        while ((row = ReadRow(reader)) is not null)
        {
            if (row.Count <= seriesIndex || row.Count <= issueIndex)
            {
                continue;
            }

            var series = row[seriesIndex].Trim();
            var issueMatch = LeadingIssueNumberRegex.Match(row[issueIndex].Trim());
            if (series.Length == 0 || !issueMatch.Success || !int.TryParse(issueMatch.Groups[1].Value, out var issueNumber))
            {
                continue;
            }

            var releaseDate = releaseDateIndex >= 0 && releaseDateIndex < row.Count
                ? ParseDate(row[releaseDateIndex])
                : null;

            rows.Add(new ClzIssueRelease
            {
                Series = series,
                NormalizedSeries = TitleNormalizer.Normalize(series),
                IssueNumber = issueNumber,
                ReleaseDate = releaseDate,
                ImportedAt = importedAt,
            });
        }
        return rows;
    }

    public static List<ClzSeriesSummary> ParseAndAggregate(TextReader reader, DateTime importedAt)
    {
        var header = ReadRow(reader);
        if (header is null)
        {
            return new List<ClzSeriesSummary>();
        }

        var seriesIndex = header.IndexOf("Series");
        var releaseDateIndex = header.IndexOf("Release Date");
        if (seriesIndex < 0)
        {
            throw new InvalidDataException("CSV is missing a \"Series\" column - is this a CLZ comics export?");
        }

        var bySeriesName = new Dictionary<string, (string Series, DateOnly? LastReleaseDate, int Count)>();

        List<string>? row;
        while ((row = ReadRow(reader)) is not null)
        {
            if (row.Count <= seriesIndex)
            {
                continue;
            }

            var series = row[seriesIndex].Trim();
            if (series.Length == 0)
            {
                continue;
            }

            var releaseDate = releaseDateIndex >= 0 && releaseDateIndex < row.Count
                ? ParseDate(row[releaseDateIndex])
                : null;

            var normalized = TitleNormalizer.Normalize(series);
            if (bySeriesName.TryGetValue(normalized, out var existing))
            {
                var newest = MaxDate(existing.LastReleaseDate, releaseDate);
                bySeriesName[normalized] = (existing.Series, newest, existing.Count + 1);
            }
            else
            {
                bySeriesName[normalized] = (series, releaseDate, 1);
            }
        }

        return bySeriesName.Select(kvp => new ClzSeriesSummary
        {
            NormalizedSeries = kvp.Key,
            Series = kvp.Value.Series,
            LastReleaseDate = kvp.Value.LastReleaseDate,
            IssueCount = kvp.Value.Count,
            ImportedAt = importedAt,
        }).ToList();
    }

    private static DateOnly? MaxDate(DateOnly? a, DateOnly? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a > b ? a : b;
    }

    private static DateOnly? ParseDate(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        // Very old back issues sometimes have only a year on record.
        if (DateOnly.TryParseExact(raw, "yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var yearOnly))
        {
            return yearOnly;
        }

        // Last resort: culture-aware general parsing catches anything the exact formats above miss.
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var general))
        {
            return general;
        }

        return null;
    }

    /// <summary>
    /// Minimal RFC4180-style CSV row reader: handles quoted fields, commas inside quotes,
    /// and "" as an escaped quote. Does not handle a quoted field spanning multiple lines -
    /// not observed in real CLZ exports this session, so not worth the extra complexity.
    /// </summary>
    private static List<string>? ReadRow(TextReader reader)
    {
        var line = reader.ReadLine();
        if (line is null)
        {
            return null;
        }

        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
