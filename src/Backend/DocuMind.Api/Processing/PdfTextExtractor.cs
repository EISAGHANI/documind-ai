using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocuMind.Api.Processing;

public record PageText(int PageNumber, string Text);

public static class PdfTextExtractor
{
    // Extracts text page by page. Scanned (image-only) pages return empty text; OCR is not supported yet.
    public static IReadOnlyList<PageText> Extract(Stream pdf)
    {
        using var document = PdfDocument.Open(pdf);

        var pages = new List<PageText>(document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            // ContentOrderTextExtractor keeps reading order and line breaks, unlike page.Text
            var text = ContentOrderTextExtractor.GetText(page);
            pages.Add(new PageText(page.Number, text));
        }

        return pages;
    }
}
