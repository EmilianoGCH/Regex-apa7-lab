using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public static class TextUtilities
{
    // Reduce espacios repetidos y saltos de linea excesivos.
    // Ejemplo: "Garcia,   J.\n\n\nTitulo" queda como "Garcia, J.\n\nTitulo".
    public static string NormalizeWhitespace(string value)
    {
        var normalized = Regex.Replace(value, @"[ \t]+", " ").Trim();
        return Regex.Replace(normalized, @"\n{3,}", "\n\n");
    }

    // Quita acentos para comparar texto sin depender de tildes.
    // Ejemplo: "Bibliografía" y "Bibliografia" se pueden comparar como "Bibliografia".
    public static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
