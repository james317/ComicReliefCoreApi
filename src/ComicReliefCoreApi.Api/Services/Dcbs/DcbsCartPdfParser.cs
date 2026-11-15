using System.Globalization;
using System.Text.RegularExpressions;
using ComicReliefCoreApi.Api.Models.Dcbs;
using UglyToad.PdfPig;

namespace ComicReliefCoreApi.Api.Services.Dcbs;

/// <summary>
/// Reads cart items off a printed DCBS cart page - a Safari full-page capture saved as PDF,
/// which keeps a real text layer with each word's position (confirmed against a real
/// November 2026 cart: no OCR needed). The page lays items out in fixed columns under a
/// "Title / Price / Quantity / Total" header, each item's title wrapped over a few lines with
/// its product code (e.g. NOV2670108) on the line below - so the product codes mark where
/// each item ends, and a word's column (by x position) says what it is. Also accepts the
/// "Create Order from Pull List" page's "Title / Price / Discount / Qty to Add" layout,
/// skipping rows left at quantity 0 (variants not being added).
/// </summary>
public static class DcbsCartPdfParser
{
    private static readonly Regex ProductCodeRegex = new(@"^[A-Z]{3}\d{5,7}[A-Z]?$", RegexOptions.Compiled);
    private static readonly Regex QuantityRegex = new(@"^\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex PriceRegex = new(@"^\$(\d+(?:\.\d{2})?)$", RegexOptions.Compiled);

    // Per-row controls printed in the title column ("Remove from cart") and quantity column ("Update").
    private static readonly HashSet<string> RowControlWords = new(StringComparer.Ordinal) { "Remove", "from", "cart", "Update" };

    private const double ColumnSlack = 10;

    private sealed record PlacedWord(string Text, double X, double Y);

    private sealed record Columns(double HeaderY, double PriceX, double QuantityX, double? TotalX);

    /// <exception cref="InvalidDataException">The file isn't a readable PDF.</exception>
    public static IReadOnlyList<DcbsCartPdfItem> Parse(Stream pdf)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("That file couldn't be read as a PDF.", ex);
        }
        using var _ = document;

        // PDF y grows upward; stack later pages below earlier ones so one item can be
        // followed across a page break.
        var words = new List<PlacedWord>();
        double pageOffset = 0;
        foreach (var page in document.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                words.Add(new PlacedWord(word.Text, word.BoundingBox.Left, word.BoundingBox.Bottom - pageOffset));
            }
            pageOffset += page.Height;
        }

        if (FindColumns(words) is not { } columns)
        {
            return [];
        }

        var productCodes = words
            .Where(w => w.Y < columns.HeaderY && w.X < columns.PriceX && ProductCodeRegex.IsMatch(w.Text))
            .OrderByDescending(w => w.Y)
            .ToList();

        var items = new List<DcbsCartPdfItem>();
        var rowTop = columns.HeaderY;
        foreach (var code in productCodes)
        {
            // Everything between the previous item's product code and this one belongs to this item.
            var row = words.Where(w => w.Y < rowTop && w.Y >= code.Y - 0.5).ToList();
            rowTop = code.Y;

            var title = string.Join(' ', row
                .Where(w => w.X < columns.PriceX - 4 && w.Y > code.Y + 0.5 && !RowControlWords.Contains(w.Text))
                .OrderByDescending(w => Math.Round(w.Y))
                .ThenBy(w => w.X)
                .Select(w => w.Text));

            var quantityWord = row.FirstOrDefault(w =>
                w.X >= columns.QuantityX - ColumnSlack
                && (columns.TotalX is not { } totalX || w.X < totalX - ColumnSlack)
                && QuantityRegex.IsMatch(w.Text));
            int? quantity = quantityWord is null ? null : int.Parse(quantityWord.Text, CultureInfo.InvariantCulture);

            var priceWord = row.FirstOrDefault(w =>
                w.X >= columns.PriceX - ColumnSlack && w.X < columns.QuantityX - ColumnSlack && PriceRegex.IsMatch(w.Text));
            decimal? unitPrice = priceWord is null
                ? null
                : decimal.Parse(PriceRegex.Match(priceWord.Text).Groups[1].Value, CultureInfo.InvariantCulture);

            if (title.Length == 0 || quantity == 0)
            {
                continue;
            }
            items.Add(new DcbsCartPdfItem(title, code.Text, quantity, unitPrice));
        }
        return items;
    }

    private static Columns? FindColumns(List<PlacedWord> words)
    {
        foreach (var titleHeader in words.Where(w => w.Text == "Title"))
        {
            var header = words.Where(w => Math.Abs(w.Y - titleHeader.Y) < 2).ToList();
            var price = header.FirstOrDefault(w => w.Text == "Price");
            var quantity = header.FirstOrDefault(w => w.Text is "Quantity" or "Qty");
            if (price is not null && quantity is not null)
            {
                var total = header.FirstOrDefault(w => w.Text == "Total");
                return new Columns(titleHeader.Y, price.X, quantity.X, total?.X);
            }
        }
        return null;
    }
}
