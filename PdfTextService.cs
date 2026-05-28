using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

public static class PdfTextService
{
    // Extrae texto de cada pagina del PDF y agrega una marca de pagina.
    // Ejemplo: antes del texto de la pagina 3 escribe "--- Pagina 3 ---".
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

    // Cuenta caracteres utiles ignorando espacios y las marcas de pagina agregadas arriba.
    // Ejemplo: si el PDF solo tiene "--- Pagina 1 ---", devuelve 0.
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
