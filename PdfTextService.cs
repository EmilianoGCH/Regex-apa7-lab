using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

public static class PdfTextService
{
    public static string ExtractText(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);
        var text = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            text.AppendLine($"--- Pagina {page.Number} ---");
            text.AppendLine(page.Text);
            text.AppendLine();
        }

        return text.ToString().Trim();
    }

    public static int CountMeaningfulCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var withoutPageMarkers = Regex.Replace(
            text,
            @"(?im)^\s*---\s*Pagina\s+\d+\s*---\s*$",
            string.Empty,
            RegexOptions.CultureInvariant);

        return withoutPageMarkers.Count(character => !char.IsWhiteSpace(character));
    }
}
