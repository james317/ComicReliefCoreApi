namespace ComicReliefCoreApi.Api.Models.Dcbs;

/// <summary>
/// Non-manga comic-publisher category pages, scraped from DCBS's own homepage nav
/// 9/2026 (see docs/BACKLOG.md for the full nav dump and how ProductsPerPage was
/// confirmed to accept values well past its documented 100-item UI-dropdown max, which
/// is what makes crawling each of these in a single request practical). Deliberately
/// excludes manga publishers (kodansha-comics, seven-seas-entertainment, tokyopop,
/// viz-media-llc, yen-press, and the manga category itself) and non-comic categories
/// (previews-catalog - magazines, calendars, trading-cards, specials).
/// </summary>
public static class DcbsPublisherCategories
{
    public static readonly IReadOnlyList<DcbsPublisherCategory> All = new[]
    {
        new DcbsPublisherCategory("dc-comics", 1, "DC Comics"),
        new DcbsPublisherCategory("marvel-comics", 4, "Marvel Comics"),
        new DcbsPublisherCategory("image-comics", 3, "Image Comics"),
        new DcbsPublisherCategory("dark-horse", 2, "Dark Horse"),
        new DcbsPublisherCategory("archie-comics-publications", 49, "Archie Comics Publications"),
        new DcbsPublisherCategory("boom-studios", 36, "Boom! Studios"),
        new DcbsPublisherCategory("cinebook", 50, "Cinebook"),
        new DcbsPublisherCategory("drawn-quarterly", 52, "Drawn & Quarterly"),
        new DcbsPublisherCategory("dynamite-entertainment", 38, "Dynamite Entertainment"),
        new DcbsPublisherCategory("fantagraphics", 53, "Fantagraphics"),
        new DcbsPublisherCategory("idw-publishing", 37, "IDW Publishing"),
        new DcbsPublisherCategory("oni-press", 54, "Oni Press"),
        new DcbsPublisherCategory("papercutz", 55, "Papercutz"),
        new DcbsPublisherCategory("scout-comics", 46, "Scout Comics"),
        new DcbsPublisherCategory("titan-comics", 48, "Titan Comics"),
        new DcbsPublisherCategory("twomorrows-publishing", 63, "TwoMorrows Publishing"),
        new DcbsPublisherCategory("udon-entertainment", 56, "Udon Entertainment"),
        new DcbsPublisherCategory("valiant-entertainment", 61, "Valiant Entertainment"),
        new DcbsPublisherCategory("vault-comics", 47, "Vault Comics"),
        new DcbsPublisherCategory("other", 6, "Other"),
    };
}
