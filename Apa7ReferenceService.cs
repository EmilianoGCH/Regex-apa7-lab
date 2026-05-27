using System.Net;
using System.Text.RegularExpressions;

public static class Apa7ReferenceService
{
    private const string DatePattern = @"\(\s*(?:n\.?\s*d\.?|s\.?\s*f\.?|(?:\d{1,2}\s+de\s+[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+\s+de\s+)?(?:19|20)\d{2}[a-z]?)(?:,\s*[^)]{1,40})?\s*\)";
    private const string BoundaryWarning = "Advertencia: No se pudo delimitar la referencia, verificar manualmente.";
    private const string RemovedPageHeaderWarningPrefix = "Advertencia: Se detectó y eliminó encabezado/pie de página incrustado:";
    private const string MixedFormatsWarning = "Advertencia: Bloque con formatos mixtos detectado — se separaron y clasificaron individualmente.";
    private const string InitialsPattern = @"(?:[A-ZÁÉÍÓÚÜÑ][a-záéíóúüñ]{0,2}\.?\s*(?:de\s+|del\s+|de la\s+)?)";
    private const string SurnamePattern = @"[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+(?:\s+[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+){0,2}";
    private const string PersonAuthorPattern = SurnamePattern + @",\s*(?:" + InitialsPattern + @"){1,4}(?:,\s*&\s*|,\s*|;\s*&?\s*|&\s*|,\s*y\s*|\s+y\s+)?";
    private const string GroupAuthorPattern = @"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9&'\-]{2,}(?:\s+(?:de|del|la|las|los|el|en|y|e|of|the|and|for|[A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9&'\-]{2,})){0,14}\.\s*(?:\([^)]{2,40}\)\.\s*)?";
    private const string IeeeReferenceStartPattern = SurnamePattern + @",\s*(?:" + InitialsPattern + @"){1,4}(?:,\s+|:\s+)";

    public static List<string> FindReferences(string text)
    {
        var section = ExtractReferenceSection(text);

        if (section.Length == 0)
        {
            return [];
        }

        var references = SplitReferences(section);
        return references.Count > 0 ? references : [section];
    }

    public static IReadOnlyList<ParentheticalCitation> FindParentheticalCitations(string text)
    {
        var bodyPages = ExtractBodyPagesBeforeReferenceSection(text);

        if (bodyPages.Count == 0)
        {
            return [];
        }

        var matches = bodyPages
            .SelectMany(page => Regex.Matches(
                    page.Text,
                    @"\((?<citation>[^()]{2,220}(?:19|20)\d{2}[a-z]?[^()]*)\)",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(match => new CitationMatch(
                    TextUtilities.NormalizeWhitespace($"({match.Groups["citation"].Value})"),
                    page.PageNumber)))
            .Where(match => IsLikelyParentheticalCitation(match.Citation))
            .ToList();

        return matches
            .GroupBy(match => match.Citation, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ParentheticalCitation(
                group.First().Citation,
                group.Count(),
                group.Select(match => match.PageNumber).Distinct().Order().ToList()))
            .OrderBy(citation => citation.Citation, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ParentheticalCitation> FindTextCitationsByFirstAuthor(
        string text,
        IEnumerable<string> compliantReferences)
    {
        var bodyPages = ExtractBodyPagesBeforeReferenceSection(text);

        if (bodyPages.Count == 0)
        {
            return [];
        }

        var firstAuthors = compliantReferences
            .Select(ExtractFirstAuthorSearchTerm)
            .Where(author => author.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (firstAuthors.Count == 0)
        {
            return [];
        }

        var citations = new List<CitationMatch>();

        foreach (var author in firstAuthors)
        {
            var normalizedAuthor = TextUtilities.RemoveDiacritics(author);
            var pattern = BuildWholeTextPattern(normalizedAuthor);

            foreach (var page in bodyPages)
            {
                var normalizedText = TextUtilities.RemoveDiacritics(page.Text);
                var count = Regex.Matches(
                    normalizedText,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;

                for (var index = 0; index < count; index++)
                {
                    citations.Add(new CitationMatch(author, page.PageNumber));
                }
            }
        }

        return citations
            .GroupBy(citation => citation.Citation, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ParentheticalCitation(
                group.First().Citation,
                group.Count(),
                group.Select(match => match.PageNumber).Distinct().Order().ToList()))
            .OrderBy(citation => citation.Citation, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ParentheticalCitation> FindNarrativeCitationsByFirstAuthor(
        string text,
        IEnumerable<string> compliantReferences)
    {
        var bodyPages = ExtractBodyPagesBeforeReferenceSection(text);

        if (bodyPages.Count == 0)
        {
            return [];
        }

        var firstAuthorYears = compliantReferences
            .Select(reference => new
            {
                Author = ExtractFirstAuthorSearchTerm(reference),
                Year = ExtractReferenceYear(reference)
            })
            .Where(item => item.Author.Length > 0 && item.Year.Length > 0)
            .DistinctBy(item => $"{item.Author}|{item.Year}", StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (firstAuthorYears.Count == 0)
        {
            return [];
        }

        var citations = new List<CitationMatch>();

        foreach (var item in firstAuthorYears)
        {
            var normalizedAuthor = TextUtilities.RemoveDiacritics(item.Author);
            var authorPattern = BuildWholeTextPattern(normalizedAuthor);
            var narrativePattern =
                $@"(?<!\(){authorPattern}(?:\s+(?:et\s+al\.?|y\s+cols?\.?|y\s+colaboradores|y\s+otros|and\s+others|&\s+[^()]{1,60}))?\s*\(\s*{Regex.Escape(item.Year)}[a-z]?\s*\)";

            foreach (var page in bodyPages)
            {
                var normalizedText = TextUtilities.RemoveDiacritics(page.Text);
                var count = Regex.Matches(
                    normalizedText,
                    narrativePattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;

                for (var index = 0; index < count; index++)
                {
                    citations.Add(new CitationMatch(item.Author, page.PageNumber));
                }
            }
        }

        return citations
            .GroupBy(citation => citation.Citation, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ParentheticalCitation(
                group.First().Citation,
                group.Count(),
                group.Select(match => match.PageNumber).Distinct().Order().ToList()))
            .OrderBy(citation => citation.Citation, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static Apa7DocumentAnalysis AnalyzeReferences(IReadOnlyList<string> references)
    {
        var detectedStyles = references
            .Select(reference => DetectCitationStyle(RemoveEmbeddedHeaderFooterNoise(
                TextUtilities.NormalizeWhitespace(reference)).CleanedReference))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var hasMixedFormatBlock = detectedStyles.Count > 1;

        var analyzedReferences = references
            .Select((reference, index) => AnalyzeReference(index + 1, reference, hasMixedFormatBlock))
            .ToList();

        return new Apa7DocumentAnalysis(
            analyzedReferences.Count,
            analyzedReferences.Count(reference => reference.IsApa7Compliant),
            analyzedReferences.Count(reference => reference.AnalysisStatus == "Apa7WithErrors"),
            analyzedReferences.Count(reference => reference.AnalysisStatus == "Apa7Invalid"),
            analyzedReferences.Count(reference => reference.AnalysisStatus == "OtherFormat"),
            analyzedReferences.Count(reference => reference.Reasons.Any(reason => reason.Contains("delimitar", StringComparison.OrdinalIgnoreCase))),
            analyzedReferences);
    }

    public static SortedDictionary<int, int> CountYears(IEnumerable<string> references)
    {
        var counts = new SortedDictionary<int, int>();

        foreach (var reference in references)
        {
            var match = Regex.Match(
                reference,
                @"\(\s*(?:\d{1,2}\s+de\s+[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+\s+de\s+)?(?<year>(?:19|20)\d{2})[a-z]?\s*\)",
                RegexOptions.CultureInvariant);

            if (!match.Success || !int.TryParse(match.Groups["year"].Value, out var year))
            {
                continue;
            }

            counts[year] = counts.TryGetValue(year, out var currentCount)
                ? currentCount + 1
                : 1;
        }

        return counts;
    }

    public static SortedDictionary<string, int> CountFirstAuthors(IEnumerable<string> references)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var reference in references)
        {
            var firstAuthor = ExtractFirstAuthor(reference);

            if (firstAuthor.Length == 0)
            {
                continue;
            }

            counts[firstAuthor] = counts.TryGetValue(firstAuthor, out var currentCount)
                ? currentCount + 1
                : 1;
        }

        return counts;
    }

    public static SortedDictionary<string, int> CountPublishers(IEnumerable<ReferenceAnalysis> references)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var reference in references)
        {
            var publisher = ExtractPublisherOrSource(reference);

            counts[publisher] = counts.TryGetValue(publisher, out var currentCount)
                ? currentCount + 1
                : 1;
        }

        return counts;
    }

    public static void WriteToTerminal(string fileName, IReadOnlyList<string> references)
    {
        Console.WriteLine();
        Console.WriteLine($"===== {fileName} =====");
        Console.WriteLine("Busqueda APA 7: Autor. (Fecha). Titulo. Fuente. DOI/URL");

        if (references.Count == 0)
        {
            Console.WriteLine("No se encontraron secciones de bibliografia.");
            return;
        }

        for (var index = 0; index < references.Count; index++)
        {
            Console.WriteLine($"--- Coincidencia {index + 1} ---");
            Console.WriteLine(references[index]);
        }
    }

    private static string ExtractReferenceSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var startIndex = -1;

        for (var index = 0; index < lines.Length; index++)
        {
            var normalizedLine = TextUtilities.RemoveDiacritics(lines[index]).ToUpperInvariant();

            if (Regex.IsMatch(
                normalizedLine,
                @"\b(?:REFERENCIAS(?:\s+BIBLIOGRAFICAS)?|BIBLIOGRAFIA|REFERENCES)\b",
                RegexOptions.CultureInvariant))
            {
                startIndex = index;
            }
        }

        if (startIndex < 0)
        {
            return string.Empty;
        }

        var repeatedInstitutionHeaders = FindRepeatedInstitutionHeaderLines(lines.Skip(startIndex));
        var sectionLines = new List<string>();

        for (var index = startIndex; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.Length == 0 || IsStandaloneHeaderFooterNoiseLine(line, repeatedInstitutionHeaders))
            {
                continue;
            }

            var normalizedLine = TextUtilities.RemoveDiacritics(line).ToUpperInvariant();

            if (index > startIndex &&
                Regex.IsMatch(
                    normalizedLine,
                    @"^(?:[IVXLCDM]+|\d+)?[\.\)]?\s*(?:ANEXOS?|APENDICES?|CONCLUSIONES?|AGRADECIMIENTOS?|GLOSARIO|RESUMEN|ABSTRACT|INTRODUCCION)\b",
                    RegexOptions.CultureInvariant))
            {
                break;
            }

            if (line.Contains("Firma del alumno", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            sectionLines.Add(line);
        }

        var section = TextUtilities.NormalizeWhitespace(string.Join(' ', sectionLines));
        return RemoveReferenceSectionTitle(section);
    }

    private static string ExtractTextBeforeReferenceSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var startIndex = -1;

        for (var index = 0; index < lines.Length; index++)
        {
            var normalizedLine = TextUtilities.RemoveDiacritics(lines[index]).ToUpperInvariant();

            if (Regex.IsMatch(
                normalizedLine,
                @"\b(?:REFERENCIAS(?:\s+BIBLIOGRAFICAS)?|BIBLIOGRAFIA|REFERENCES)\b",
                RegexOptions.CultureInvariant))
            {
                startIndex = index;
                break;
            }
        }

        var bodyLines = startIndex < 0 ? lines : lines.Take(startIndex);
        var body = string.Join(' ', bodyLines);

        return TextUtilities.NormalizeWhitespace(body);
    }

    private static List<BodyPageText> ExtractBodyPagesBeforeReferenceSection(string text)
    {
        var pages = new List<BodyPageText>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return pages;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var currentPage = 1;
        var pageText = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var pageMatch = Regex.Match(
                line,
                @"^---\s*Pagina\s+(?<page>\d+)\s*---$",
                RegexOptions.CultureInvariant);

            if (pageMatch.Success)
            {
                AddBodyPage(pages, currentPage, pageText);
                currentPage = int.Parse(pageMatch.Groups["page"].Value);
                continue;
            }

            var normalizedLine = TextUtilities.RemoveDiacritics(line).ToUpperInvariant();

            if (Regex.IsMatch(
                normalizedLine,
                @"\b(?:REFERENCIAS(?:\s+BIBLIOGRAFICAS)?|BIBLIOGRAFIA|REFERENCES)\b",
                RegexOptions.CultureInvariant))
            {
                AddBodyPage(pages, currentPage, pageText);
                break;
            }

            pageText.Add(line);
        }

        AddBodyPage(pages, currentPage, pageText);
        return pages;
    }

    private static void AddBodyPage(List<BodyPageText> pages, int pageNumber, List<string> pageText)
    {
        var normalizedText = TextUtilities.NormalizeWhitespace(string.Join(' ', pageText));
        pageText.Clear();

        if (normalizedText.Length > 0)
        {
            pages.Add(new BodyPageText(pageNumber, normalizedText));
        }
    }

    private static HashSet<string> FindRepeatedInstitutionHeaderLines(IEnumerable<string> lines)
    {
        return lines
            .Select(CanonicalHeaderFooterLine)
            .Where(line => IsLikelyInstitutionHeaderLine(line))
            .GroupBy(line => line, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsStandaloneHeaderFooterNoiseLine(
        string line,
        IReadOnlySet<string> repeatedInstitutionHeaders)
    {
        var trimmed = TextUtilities.NormalizeWhitespace(line).Trim();

        if (trimmed.Length == 0)
        {
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^---\s*Pagina\s+\d+\s*---$", RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^\d{1,4}$", RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (Regex.IsMatch(
            trimmed,
            @"^(?:Page\s+\d+\s+of\s+\d+|P[aá]gina\s+\d+\s+(?:de|/)\s+\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        if (IsHeaderFooterNoiseFragment(trimmed))
        {
            return true;
        }

        var canonicalLine = CanonicalHeaderFooterLine(trimmed);
        return repeatedInstitutionHeaders.Contains(canonicalLine);
    }

    private static string CanonicalHeaderFooterLine(string line)
    {
        return TextUtilities.RemoveDiacritics(TextUtilities.NormalizeWhitespace(line))
            .Trim()
            .Trim('.', ':', '-', '–', '—')
            .ToUpperInvariant();
    }

    private static bool IsLikelyInstitutionHeaderLine(string canonicalLine)
    {
        if (canonicalLine.Length is < 8 or > 130)
        {
            return false;
        }

        if (Regex.IsMatch(canonicalLine, DatePattern, RegexOptions.CultureInvariant))
        {
            return false;
        }

        return Regex.IsMatch(
            canonicalLine,
            @"\b(?:UNIVERSIDAD|INSTITUTO|TECNOLOGICO|FACULTAD|ESCUELA|DEPARTAMENTO|CENTRO\s+UNIVERSITARIO|SECRETARIA|MINISTERIO)\b",
            RegexOptions.CultureInvariant);
    }

    private static bool IsHeaderFooterNoiseFragment(string text)
    {
        return Regex.IsMatch(
            text,
            @"^(?:" +
            @"(?:F|FOR)-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})*" +
            @"|PROTOCOLO\s+DE\s+INVESTIGACI[ÓO]N" +
            @"|TRABAJO\s+DE\s+GRADO" +
            @"|TESIS" +
            @"|TRABAJO\s+TERMINAL" +
            @"|Nivel\s+de\s+revisi[oó]n\s*:\s*\d+" +
            @"|(?:Enero|Febrero|Marzo|Abril|Mayo|Junio|Julio|Agosto|Septiembre|Setiembre|Octubre|Noviembre|Diciembre|January|February|March|April|May|June|July|August|September|October|November|December)\s+(?:19|20)\d{2}" +
            @"|(?:19|20)\d{2}-\d{2}(?:-\d{2})?" +
            @")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string RemoveReferenceSectionTitle(string section)
    {
        var withoutLeadingTitle = Regex.Replace(
            section,
            @"(?is)^\s*(?:[#\s]*\d+|[IVXLCDM]+)?[\.\)]?\s*(?:referencias\s*bibliogr[aá]ficas|referencias|bibliograf[ií]a|references)\b[:\s\-–—]*",
            string.Empty,
            RegexOptions.CultureInvariant);

        return Regex.Replace(
            withoutLeadingTitle,
            @"(?is)(?<=[\s#\d\.\)])(?:referencias\s*bibliogr[aá]ficas|referencias|bibliograf[ií]a|references)\b[:\s\-–—]*(?=[A-ZÁÉÍÓÚÑ])",
            string.Empty,
            RegexOptions.CultureInvariant).Trim();
    }

    private static List<string> SplitReferences(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return [];
        }

        var text = RemoveIeeeInterReferencePageNumbers(
            TextUtilities.NormalizeWhitespace(RemoveReferenceSectionTitle(section)));

        var starts = FindAllReferenceStartIndexes(text)
            .ToList();

        if (starts.Count == 0)
        {
            return [];
        }

        var references = new List<string>();

        for (var index = 0; index < starts.Count; index++)
        {
            var start = starts[index];
            var end = index + 1 < starts.Count ? starts[index + 1] : text.Length;
            var reference = CleanReference(text[start..end]);

            if (reference.Length > 0)
            {
                references.Add(reference);
            }
        }

        return references;
    }

    private static List<int> FindAllReferenceStartIndexes(string text)
    {
        var apaStarts = FindReferenceStartMatches(text)
            .Select(match => match.Index);
        var ieeeStarts = FindIeeeReferenceStartMatches(text)
            .Select(match => match.Index);

        return apaStarts
            .Concat(ieeeStarts)
            .Distinct()
            .Order()
            .ToList();
    }

    private static bool IsLikelyReferenceStart(string text, int startIndex)
    {
        var lookBehindStart = Math.Max(0, startIndex - 120);
        var before = text[lookBehindStart..startIndex];

        if (before.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            before.Contains("doi.org", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (startIndex == 0 ||
            before.EndsWith(". ", StringComparison.Ordinal) ||
            before.EndsWith("\n", StringComparison.Ordinal))
        {
            return true;
        }

        var previousText = text[Math.Max(0, startIndex - 12)..startIndex];
        return Regex.IsMatch(previousText, @"(?:^|\s)$", RegexOptions.CultureInvariant);
    }

    private static string CleanReference(string reference)
    {
        var normalized = TextUtilities.NormalizeWhitespace(reference);
        normalized = RemoveReferenceSectionTitle(normalized);

        return Regex.Replace(
            normalized,
            @"^[#\s]*\d+[\.\)]?\s*(?=[A-ZÁÉÍÓÚÑ])",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static string RemoveIeeeInterReferencePageNumbers(string text)
    {
        return Regex.Replace(
            text,
            $@"(?<yearEnd>\(\s*(?:19|20)\d{{2}}[a-z]?\s*\)\.?)\s+\d{{1,4}}\s+(?={IeeeReferenceStartPattern})",
            match => $"{match.Groups["yearEnd"].Value} ",
            RegexOptions.CultureInvariant);
    }

    private static List<Match> FindIeeeReferenceStartMatches(string text)
    {
        return Regex.Matches(
                text,
                $@"(?<!\p{{L}})(?<entryStart>{IeeeReferenceStartPattern})",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Where(match => IsLikelyIeeeReferenceStart(text, match.Index))
            .DistinctBy(match => match.Index)
            .OrderBy(match => match.Index)
            .ToList();
    }

    private static bool IsLikelyIeeeReferenceStart(string text, int startIndex)
    {
        if (startIndex == 0)
        {
            return true;
        }

        var before = text[Math.Max(0, startIndex - 40)..startIndex];
        return Regex.IsMatch(
            before,
            @"\(\s*(?:19|20)\d{2}[a-z]?\s*\)\.?\s+(?:\d{1,4}\s+)?$",
            RegexOptions.CultureInvariant);
    }

    private static HeaderFooterNoiseRemoval RemoveEmbeddedHeaderFooterNoise(string reference)
    {
        var removedFragments = new List<string>();
        var cleaned = reference;

        var patterns = new[]
        {
            @"\b(?:F|FOR)-[A-Z0-9]{2,}(?:-[A-Z0-9]{2,})*\b",
            @"\bPROTOCOLO\s+DE\s+INVESTIGACI[ÓO]N\b",
            @"\bTRABAJO\s+DE\s+GRADO\b",
            @"\bTESIS\b",
            @"\bTRABAJO\s+TERMINAL\b",
            @"\bNivel\s+de\s+revisi[oó]n\s*:\s*\d+\b",
            @"\bPage\s+\d+\s+of\s+\d+\b",
            @"\bP[aá]gina\s+\d+\s+(?:de|/)\s+\d+\b",
            @"\b(?:Enero|Febrero|Marzo|Abril|Mayo|Junio|Julio|Agosto|Septiembre|Setiembre|Octubre|Noviembre|Diciembre|January|February|March|April|May|June|July|August|September|October|November|December)\s+(?:19|20)\d{2}\b",
            @"\b(?:19|20)\d{2}-\d{2}(?:-\d{2})?\b"
        };

        foreach (var pattern in patterns)
        {
            cleaned = Regex.Replace(
                cleaned,
                pattern,
                match =>
                {
                    var fragment = TextUtilities.NormalizeWhitespace(match.Value).Trim();

                    if (fragment.Length > 0 &&
                        !removedFragments.Contains(fragment, StringComparer.CurrentCultureIgnoreCase))
                    {
                        removedFragments.Add(fragment);
                    }

                    return " ";
                },
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return new HeaderFooterNoiseRemoval(
            TextUtilities.NormalizeWhitespace(cleaned),
            removedFragments);
    }

    private static bool IsLikelyParentheticalCitation(string citation)
    {
        var inner = citation.Trim().TrimStart('(').TrimEnd(')');

        if (!Regex.IsMatch(inner, @"(?:19|20)\d{2}[a-z]?", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!Regex.IsMatch(inner, @"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!inner.Contains(',', StringComparison.Ordinal) &&
            !inner.Contains(';', StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(
            inner,
            @"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ&'\-\. ]{1,100}\s*,\s*(?:19|20)\d{2}[a-z]?",
            RegexOptions.CultureInvariant);
    }

    private static string ExtractFirstAuthor(string reference)
    {
        var dateMatch = Regex.Match(
            reference,
            DatePattern,
            RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return string.Empty;
        }

        var authorText = TextUtilities.NormalizeWhitespace(reference[..dateMatch.Index]);

        var personMatch = Regex.Match(
            authorText,
            @"^(?<author>(?:[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+\s+){0,2}[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+,\s*(?:" + InitialsPattern + @"){1,4})",
            RegexOptions.CultureInvariant);

        if (personMatch.Success)
        {
            return TextUtilities.NormalizeWhitespace(personMatch.Groups["author"].Value).TrimEnd('.');
        }

        var groupAuthor = authorText.Trim();

        if (groupAuthor.EndsWith(".", StringComparison.Ordinal))
        {
            groupAuthor = groupAuthor[..^1];
        }

        return groupAuthor;
    }

    private static string ExtractFirstAuthorSearchTerm(string reference)
    {
        var firstAuthor = ExtractFirstAuthor(reference);

        if (firstAuthor.Length == 0)
        {
            return string.Empty;
        }

        var commaIndex = firstAuthor.IndexOf(',', StringComparison.Ordinal);
        return commaIndex >= 0
            ? TextUtilities.NormalizeWhitespace(firstAuthor[..commaIndex]).TrimEnd('.')
            : firstAuthor;
    }

    private static string ExtractReferenceYear(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return string.Empty;
        }

        var yearMatch = Regex.Match(
            dateMatch.Value,
            @"(?:19|20)\d{2}",
            RegexOptions.CultureInvariant);

        return yearMatch.Success ? yearMatch.Value : string.Empty;
    }

    private static string BuildWholeTextPattern(string text)
    {
        var escapedWords = TextUtilities.NormalizeWhitespace(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Regex.Escape);

        return $@"(?<![\p{{L}}\p{{N}}]){string.Join(@"\s+", escapedWords)}(?![\p{{L}}\p{{N}}])";
    }

    private static string ExtractPublisherOrSource(ReferenceAnalysis reference)
    {
        var source = reference.ReferenceType switch
        {
            "Articulo de revista" => ExtractJournalName(reference.Reference),
            "Libro" => ExtractLastSourcePart(reference.Reference),
            "Capitulo de libro" => ExtractLastSourcePart(reference.Reference),
            "Capitulo de libro de actas" => ExtractLastSourcePart(reference.Reference),
            "Ponencia en conferencia" => ExtractLastSourcePart(reference.Reference),
            "Reporte institucional" => ExtractLastSourcePart(reference.Reference),
            "Sitio web" => ExtractWebsiteName(reference.Reference),
            _ => string.Empty
        };

        return NormalizeSourceName(source);
    }

    private static string ExtractJournalName(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return string.Empty;
        }

        var afterDate = reference[(dateMatch.Index + dateMatch.Length)..].Trim();
        var match = Regex.Match(
            afterDate,
            @"\.\s*(?<journal>[^.]{2,160}),\s*\d+",
            RegexOptions.CultureInvariant);

        return match.Success
            ? CleanSourceCandidate(match.Groups["journal"].Value)
            : string.Empty;
    }

    private static string ExtractLastSourcePart(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return string.Empty;
        }

        var beforeUrl = Regex.Replace(
            reference[(dateMatch.Index + dateMatch.Length)..],
            @"https?://\S+",
            string.Empty,
            RegexOptions.CultureInvariant);
        var parts = beforeUrl
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Where(part => !part.StartsWith("Recuperado", StringComparison.OrdinalIgnoreCase))
            .Where(part => !Regex.IsMatch(part, @"^\(?pp\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();

        return parts.Count >= 2
            ? CleanSourceCandidate(parts[^1])
            : string.Empty;
    }

    private static string ExtractWebsiteName(string reference)
    {
        var urlMatch = Regex.Match(reference, @"https?://\S+", RegexOptions.CultureInvariant);

        if (!urlMatch.Success)
        {
            return string.Empty;
        }

        var beforeUrl = reference[..urlMatch.Index].Trim();
        var dateMatch = Regex.Match(beforeUrl, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return string.Empty;
        }

        var afterDate = beforeUrl[(dateMatch.Index + dateMatch.Length)..].Trim();
        var parts = afterDate
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        return parts.Count >= 2
            ? CleanSourceCandidate(parts[^1])
            : string.Empty;
    }

    private static string NormalizeSourceName(string source)
    {
        var cleaned = CleanSourceCandidate(source);
        return IsValidSourceName(cleaned) ? cleaned : "No Identificado";
    }

    private static string CleanSourceCandidate(string source)
    {
        var decodedSource = WebUtility.HtmlDecode(Regex.Replace(
            source,
            @"&amp;",
            "&",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

        var cleaned = TextUtilities.NormalizeWhitespace(decodedSource)
            .Trim()
            .Trim(',', ';', ':', '.', '-', '–', '—', '&', '*', '•', '·', ')', '(', '[', ']');

        cleaned = Regex.Replace(
            cleaned,
            @"\s*,\s*\d+\s*(?:\([^)]*\))?(?:\s*,.*)?$",
            string.Empty,
            RegexOptions.CultureInvariant);

        return cleaned.Trim()
            .Trim(',', ';', ':', '.', '-', '–', '—', '&', '*', '•', '·', ')', '(', '[', ']');
    }

    private static bool IsValidSourceName(string source)
    {
        if (source.Length < 3)
        {
            return false;
        }

        if (!Regex.IsMatch(source, @"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{3,}", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (Regex.IsMatch(source, @"^(?:\d+|\d+\)|[ivxlcdm]+\.?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (Regex.IsMatch(source, @"^[,&;:\-\s•·]+", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return source.Count(char.IsLetter) >= 3;
    }

    private static ReferenceAnalysis AnalyzeReference(
        int number,
        string reference,
        bool hasMixedFormatBlock)
    {
        var cleanup = RemoveEmbeddedHeaderFooterNoise(TextUtilities.NormalizeWhitespace(reference));
        var normalized = cleanup.CleanedReference;
        var reasons = new List<string>();

        if (hasMixedFormatBlock)
        {
            reasons.Add(MixedFormatsWarning);
        }

        reasons.AddRange(cleanup.RemovedFragments
            .Select(fragment => $"{RemovedPageHeaderWarningPrefix} [{fragment}]"));

        var citationStyle = DetectCitationStyle(normalized);
        var referenceType = citationStyle == "APA 7" ? ClassifyReference(normalized) : "Otro formato";
        var isNonStandardApaType = false;

        if (citationStyle != "APA 7")
        {
            if (IsRecognizedNonApaFormat(citationStyle))
            {
                reasons.Add($"Otro formato: {citationStyle} — no aplica análisis APA 7.");

                return new ReferenceAnalysis(
                    number,
                    normalized,
                    referenceType,
                    citationStyle,
                    "OtherFormat",
                    false,
                    reasons);
            }

            reasons.Add("No cumple: la referencia no inicia con autor APA 7 en formato Apellido, iniciales o institucion seguido de fecha entre parentesis.");

            return new ReferenceAnalysis(
                number,
                normalized,
                "Ambiguo",
                citationStyle,
                "Apa7Invalid",
                false,
                reasons);
        }

        if (Regex.IsMatch(normalized, @"\((?:[^)]*Entrevistador[^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            referenceType = "Tipo no estandar";
            isNonStandardApaType = true;
            reasons.Add("No cumple: referencia con entrevistador; no es una referencia APA 7 estandar salvo que la entrevista este publicada con datos completos.");
        }

        if (normalized.Contains("...", StringComparison.Ordinal) ||
            normalized.Contains("…", StringComparison.Ordinal))
        {
            reasons.Add("No cumple: la referencia parece incompleta porque contiene puntos suspensivos.");
        }

        if (Regex.IsMatch(normalized, @"^\s*\[\d+\]", RegexOptions.CultureInvariant))
        {
            reasons.Add("No cumple: usa numeracion por corchetes al inicio.");
        }

        if (HasAmbiguousReferenceBoundary(normalized))
        {
            reasons.Add(BoundaryWarning);
        }

        var dateMatch = Regex.Match(normalized, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            reasons.Add("No cumple: no tiene año o fecha entre parentesis despues del autor.");
        }
        else if (dateMatch.Index == 0)
        {
            reasons.Add("No cumple: la fecha aparece sin autor antes.");
        }

        var authorText = dateMatch.Success
            ? TextUtilities.NormalizeWhitespace(normalized[..dateMatch.Index])
            : string.Empty;

        var hasPersonalAuthor = Regex.IsMatch(authorText, $@"^(?:{PersonAuthorPattern})+", RegexOptions.CultureInvariant);
        var hasGroupAuthor = Regex.IsMatch(authorText, $@"^{GroupAuthorPattern}$", RegexOptions.CultureInvariant);

        if (!hasPersonalAuthor && !hasGroupAuthor)
        {
            reasons.Add("No cumple: el autor no sigue el formato Apellido, iniciales o institucion.");
        }

        if (hasGroupAuthor && IsLikelyUnexpandedInstitution(authorText))
        {
            reasons.Add("No cumple: autor institucional incompleto o sigla sin nombre completo de la institucion.");
        }

        if (hasPersonalAuthor && HasMultiplePersonalAuthors(authorText) && !authorText.Contains('&'))
        {
            reasons.Add("No cumple: hay varios autores y falta & antes del ultimo autor.");
        }

        if (LooksLikeDoi(normalized) && !normalized.Contains("https://doi.org/", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("No cumple: el DOI no esta como URL completa https://doi.org/...");
        }

        if (normalized.Contains("orcid.org", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("No cumple: usa ORCID como identificador de fuente; ORCID identifica autores, no articulos.");
        }

        if (HasUnbalancedQuotationMarks(normalized))
        {
            reasons.Add("No cumple: nombre de revista, titulo o fuente con comillas sin cerrar o incompletas.");
        }

        if (HasNoDateWithUnreliableSource(normalized))
        {
            reasons.Add("No cumple: usa (n.d.) o (s.f.) sin fuente identificable o URL confiable.");
        }

        if (HasLikelyCompoundSurname(authorText))
        {
            reasons.Add("Advertencia: apellido compuesto detectado; verificar manualmente con la fuente original.");
        }

        if (UsesOldRetrievalPhrase(normalized))
        {
            reasons.Add("No cumple: usa 'Retrieved from', 'Recuperado de' u 'Obtenido de', propio de estilos anteriores y no requerido en APA 7.");
        }

        if (HasInitialFirstAuthor(normalized))
        {
            reasons.Add("No cumple: el autor parece estar con inicial primero en lugar de apellido primero.");
        }

        if (HasYearAtEndInsteadOfAfterAuthor(normalized))
        {
            reasons.Add("No cumple: el año aparece al final o fuera de la posicion APA despues del autor.");
        }

        if (HasManyAuthorsWithoutEllipsis(authorText))
        {
            reasons.Add("No cumple: mas de 20 autores deben usar puntos suspensivos antes del ultimo autor.");
        }

        reasons.AddRange(ValidateByType(normalized, referenceType));

        if (!HasEnoughSentencePartsAfterDate(normalized, dateMatch))
        {
            reasons.Add("No cumple: despues de la fecha no se distinguen titulo y fuente.");
        }

        var blockingReasons = reasons
            .Where(reason =>
                reason.StartsWith("No cumple:", StringComparison.Ordinal) ||
                reason.StartsWith(BoundaryWarning, StringComparison.Ordinal))
            .ToList();
        var invalidReasons = blockingReasons
            .Where(IsStructurallyInvalidApaReason)
            .ToList();

        if (blockingReasons.Count == 0)
        {
            reasons.Insert(0, "Cumple estructura APA 7 basica para el tipo detectado.");
        }

        var status = blockingReasons.Count == 0 && !isNonStandardApaType
            ? "Apa7Correct"
            : invalidReasons.Count > 0 || isNonStandardApaType
                ? "Apa7Invalid"
                : "Apa7WithErrors";

        return new ReferenceAnalysis(
            number,
            normalized,
            referenceType,
            citationStyle,
            status,
            status == "Apa7Correct",
            reasons);
    }

    private static bool IsRecognizedNonApaFormat(string citationStyle)
    {
        return citationStyle is "Formato IEEE" or "Formato Vancouver" or "Formato MLA" or "Formato Chicago";
    }

    private static bool IsStructurallyInvalidApaReason(string reason)
    {
        return reason.Contains("no inicia con autor APA 7", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("no tiene año o fecha", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("fecha aparece sin autor", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("autor no sigue", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("inicial primero", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("año aparece al final", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("referencia parece incompleta", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("despues de la fecha no se distinguen", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("tipo no estandar", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("tipo ambiguo", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectCitationStyle(string reference)
    {
        if (IsIeeeReference(reference))
        {
            return "Formato IEEE";
        }

        if (IsVancouverReference(reference))
        {
            return "Formato Vancouver";
        }

        if (IsMlaReference(reference))
        {
            return "Formato MLA";
        }

        if (IsChicagoReference(reference))
        {
            return "Formato Chicago";
        }

        if (LooksLikeApa7(reference))
        {
            return "APA 7";
        }

        return "Otro estilo";
    }

    private static bool LooksLikeApa7(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success || dateMatch.Index <= 0)
        {
            return false;
        }

        var authorText = TextUtilities.NormalizeWhitespace(reference[..dateMatch.Index]);
        return Regex.IsMatch(authorText, $@"^(?:{PersonAuthorPattern})+", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(authorText, $@"^{GroupAuthorPattern}$", RegexOptions.CultureInvariant);
    }

    private static bool HasAmbiguousReferenceBoundary(string reference)
    {
        var startMatches = FindReferenceStartMatches(reference);
        return startMatches.Count > 1 ||
            (startMatches.Count <= 1 && Regex.Matches(reference, DatePattern, RegexOptions.CultureInvariant).Count > 1);
    }

    private static List<Match> FindReferenceStartMatches(string text)
    {
        return Regex.Matches(
                text,
                $@"(?<!\p{{L}})(?<entryStart>(?:{PersonAuthorPattern}(?:(?:{PersonAuthorPattern}){{0,19}})|{GroupAuthorPattern}){DatePattern})",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Where(match => IsLikelyReferenceStart(text, match.Index))
            .DistinctBy(match => match.Index)
            .OrderBy(match => match.Index)
            .ToList();
    }

    private static bool IsIeeeReference(string reference)
    {
        return Regex.IsMatch(reference, @"^\s*\[\d+\]", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(reference, @"^[A-Z]\.\s*[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+.*,\s*[""“].+[""”].*,\s*(?:19|20)\d{2}\.?\s*$", RegexOptions.CultureInvariant) ||
            IsIeeeAuthorYearReference(reference);
    }

    private static bool IsIeeeAuthorYearReference(string reference)
    {
        var normalized = TextUtilities.NormalizeWhitespace(reference);

        if (!Regex.IsMatch(
            normalized,
            $@"^{IeeeReferenceStartPattern}",
            RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!Regex.IsMatch(
            normalized,
            @"\(\s*(?:19|20)\d{2}[a-z]?\s*\)\.?\s*$",
            RegexOptions.CultureInvariant))
        {
            return false;
        }

        return Regex.IsMatch(normalized, @"^" + IeeeReferenceStartPattern + @"[""“]?[A-ZÁÉÍÓÚÑ¿¡]", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(normalized, @",\s*(?:vol\.?|v\.|núm\.?|no\.?)?\s*\d+[^()]{0,160}\(\s*(?:19|20)\d{2}[a-z]?\s*\)\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(normalized, @",\s*pp\.?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsVancouverReference(string reference)
    {
        return Regex.IsMatch(reference, @"^\s*\d+\.\s+[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+\s+[A-Z]{1,3}\.?", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(reference, @"\b(?:J|Rev|Clin|Med|N Engl J Med|BMJ|Lancet)\b.*\b(?:19|20)\d{2};\d+", RegexOptions.CultureInvariant);
    }

    private static bool IsMlaReference(string reference)
    {
        return Regex.IsMatch(reference, @"^[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+,\s+[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+\.?\s+[""“].+[""”]\.", RegexOptions.CultureInvariant);
    }

    private static bool IsChicagoReference(string reference)
    {
        return Regex.IsMatch(reference, @"^[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+,\s+[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+\.?\s+.+\.\s+[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+:\s+.+,\s+(?:19|20)\d{2}\.", RegexOptions.CultureInvariant);
    }

    private static string ClassifyReference(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);
        var authorText = dateMatch.Success ? TextUtilities.NormalizeWhitespace(reference[..dateMatch.Index]) : string.Empty;

        if (Regex.IsMatch(reference, @"\((?:[^)]*Entrevistador[^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "Tipo no estandar";
        }

        if (IsConferencePresentation(reference))
        {
            return "Ponencia en conferencia";
        }

        if (IsProceedingsSource(reference))
        {
            return "Capitulo de libro de actas";
        }

        if (Regex.IsMatch(reference, @"\bEn\s+[A-ZÁÉÍÓÚÑ][^()]{2,80}\(Ed\.|\bEn\s+[A-ZÁÉÍÓÚÑ][^()]{2,80}\(Eds\.", RegexOptions.CultureInvariant))
        {
            return "Capitulo de libro";
        }

        if (Regex.IsMatch(authorText, $@"^{GroupAuthorPattern}$", RegexOptions.CultureInvariant))
        {
            return "Reporte institucional";
        }

        if (HasBookPublisher(reference) && !LooksLikeAcademicSource(reference))
        {
            return "Libro";
        }

        if (HasJournalVolume(reference) ||
            HasIssueOnlyJournalNumber(reference) ||
            Regex.IsMatch(reference, @"\b\d+\s*\(\s*\d+\s*\)\s*,\s*\d+", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(reference, @"\b\d+\s*,\s*\d+\s*[-–]\s*\d+", RegexOptions.CultureInvariant))
        {
            return "Articulo de revista";
        }

        if (HasBookPublisher(reference))
        {
            return "Libro";
        }

        if (Regex.IsMatch(reference, @"https?://\S+", RegexOptions.CultureInvariant))
        {
            return LooksLikeAcademicSource(reference) ? "Articulo de revista" : "Sitio web";
        }

        if (Regex.IsMatch(reference, @"\((?:[^)]*Entrevistador[^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "Tipo no estandar";
        }

        return "Ambiguo";
    }

    private static IEnumerable<string> ValidateByType(string reference, string referenceType)
    {
        var reasons = new List<string>();

        switch (referenceType)
        {
            case "Articulo de revista":
                if (!HasJournalVolume(reference) && !HasIssueOnlyJournalNumber(reference))
                {
                    reasons.Add("No cumple: articulo de revista sin volumen detectable o numero de revista entre parentesis.");
                }

                if (HasJournalVolume(reference) && !HasJournalIssue(reference))
                {
                    reasons.Add("Advertencia: posible numero de revista omitido, verificar manualmente.");
                }

                if (!HasPageRange(reference) && !HasDoiUrl(reference))
                {
                    reasons.Add("No cumple: articulo de revista sin paginas ni DOI/URL que identifique el articulo.");
                }

                if (!HasDoiUrl(reference) && !IsLikelyPreDoiArticle(reference))
                {
                    reasons.Add("No cumple: articulo de revista sin DOI o URL.");
                }

                if (reference.Contains("orcid.org", StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add("No cumple: ORCID no sustituye DOI ni URL del articulo.");
                }
                break;

            case "Capitulo de libro":
            case "Capitulo de libro de actas":
            case "Ponencia en conferencia":
                if (!Regex.IsMatch(reference, @"\bEn\s+", RegexOptions.CultureInvariant))
                {
                    if (referenceType == "Ponencia en conferencia" && Regex.IsMatch(reference, @"\[(?:Ponencia|Conference paper|Presentaci[oó]n)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        // APA 7 permite ponencias no publicadas sin "En" cuando se identifican como ponencia.
                    }
                    else
                    {
                        reasons.Add("No cumple: capitulo/proceedings sin 'En' antes del libro, actas o proceedings.");
                    }
                }

                if (!Regex.IsMatch(reference, @"\b\(Eds?\.?\)|\b\(Ed\.?\)", RegexOptions.CultureInvariant))
                {
                    if (referenceType == "Capitulo de libro de actas" || referenceType == "Ponencia en conferencia")
                    {
                        reasons.Add("Advertencia: proceedings o ponencia sin editor nombrado; en conferencias IEEE el editor frecuentemente no esta disponible.");
                    }
                    else
                    {
                        reasons.Add("No cumple: capitulo de libro sin editor en formato (Ed.) o (Eds.).");
                    }
                }

                if (!Regex.IsMatch(reference, @"\(pp\.\s*\d+\s*[-–]\s*\d+\)", RegexOptions.CultureInvariant) &&
                    !HasDoiUrl(reference))
                {
                    reasons.Add("No cumple: capitulo/proceedings sin paginas en formato (pp. xx-xx) ni DOI/URL suficiente.");
                }

                if (!HasBookPublisher(reference) && !HasDoiUrl(reference))
                {
                    reasons.Add("No cumple: capitulo/proceedings sin editorial identificable ni DOI/URL.");
                }
                break;

            case "Reporte institucional":
                if (reference.Contains("http", StringComparison.OrdinalIgnoreCase) &&
                    !Regex.IsMatch(reference, @"https?://\S+", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: enlace institucional no parece URL valida.");
                }

                if (!Regex.IsMatch(reference, @"https?://\S+", RegexOptions.CultureInvariant) && !HasInstitutionalPublisher(reference))
                {
                    reasons.Add("No cumple: documento institucional sin editorial/institucion editora ni URL.");
                }

                if (Regex.IsMatch(reference, @"^[A-Za-zÁÉÍÓÚÜÑáéíóúüñ\s]+\. \([^)]{2,40}\)\. \(", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: sigla institucional entre parentesis mal ubicada como autor separado.");
                }
                break;

            case "Libro":
                var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

                if (dateMatch.Success && Regex.IsMatch(dateMatch.Value, @"\d{1,2}\s+de\s+", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: en libro APA 7 la fecha debe ser solo año, no dia y mes.");
                }

                if (Regex.Matches(reference, @"\.").Count < 3)
                {
                    reasons.Add("No cumple: libro sin autor, fecha, titulo y editorial claramente separados por puntos.");
                }

                if (!HasBookPublisher(reference))
                {
                    reasons.Add("No cumple: libro sin editorial identificable despues del titulo.");
                }

                if (Regex.IsMatch(reference, @"\(\s*\d+(?:a|ª)\.\s*\)", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: la edicion debe escribirse como (5.ª ed.), no como (5a.).");
                }
                break;

            case "Sitio web":
                if (!Regex.IsMatch(reference, @"https?://\S+", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: sitio web sin URL.");
                }

                var websiteDate = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);
                if (websiteDate.Success &&
                    Regex.IsMatch(websiteDate.Value, @"^\(\s*(?:19|20)\d{2}[a-z]?\s*\)$", RegexOptions.CultureInvariant))
                {
                    reasons.Add("Advertencia: sitio web con solo año; verificar si requiere fecha completa (año, día de mes).");
                }

                if (UsesOldRetrievalPhrase(reference))
                {
                    reasons.Add("No cumple: sitio web usa formula de recuperacion antigua; en APA 7 se omite salvo contenido cambiante.");
                }

                if (!HasWebsiteNameBeforeUrl(reference))
                {
                    reasons.Add("No cumple: sitio web sin nombre del sitio antes de la URL.");
                }
                break;

            case "Tipo no estandar":
                reasons.Add("No cumple: tipo no estandar para esta validacion APA 7.");
                break;

            case "Ambiguo":
                reasons.Add("No cumple: tipo ambiguo — verificar manualmente.");
                break;
        }

        return reasons;
    }

    private static bool HasEnoughSentencePartsAfterDate(string reference, Match dateMatch)
    {
        if (!dateMatch.Success)
        {
            return false;
        }

        var afterDate = reference[(dateMatch.Index + dateMatch.Length)..].Trim();
        return Regex.Matches(afterDate, @"\.").Count >= 2;
    }

    private static bool LooksLikeDoi(string reference)
    {
        return reference.Contains("doi", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(reference, @"\b10\.\d{4,9}/\S+", RegexOptions.CultureInvariant);
    }

    private static bool HasMultiplePersonalAuthors(string authorText)
    {
        return Regex.Matches(
            authorText,
            @"[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+,\s*(?:" + InitialsPattern + @"){1,4}",
            RegexOptions.CultureInvariant).Count > 1;
    }

    private static bool HasBookPublisher(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return false;
        }

        var afterDate = reference[(dateMatch.Index + dateMatch.Length)..].Trim();
        var parts = afterDate
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Where(part => !part.StartsWith("Recuperado", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return parts.Count >= 2 && parts[^1].Length >= 2;
    }

    private static bool IsLikelyUnexpandedInstitution(string authorText)
    {
        var cleaned = authorText.Trim().TrimEnd('.');

        if (cleaned.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(cleaned, @"^[A-ZÁÉÍÓÚÑ/&]{2,}(?:,\s*[A-ZÁÉÍÓÚÑ]\.?)?$", RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeAcademicSource(string reference)
    {
        return HasJournalVolume(reference) ||
            Regex.IsMatch(reference, @"\b\d+\s*(?:\(\s*\d+\s*\))?\s*,\s*\d+", RegexOptions.CultureInvariant) ||
            reference.Contains("journal", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("revista", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasJournalVolume(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return false;
        }

        var afterDate = reference[(dateMatch.Index + dateMatch.Length)..];
        return Regex.IsMatch(
            afterDate,
            @"\.\s*[^.]{2,120},\s*\d+\s*(?:\(\s*\d+\s*\))?(?:,|\.)",
            RegexOptions.CultureInvariant);
    }

    private static bool HasIssueOnlyJournalNumber(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return false;
        }

        var afterDate = reference[(dateMatch.Index + dateMatch.Length)..];
        return Regex.IsMatch(
            afterDate,
            @"\.\s*[^.]{2,120},\s*\(\s*\d+\s*\)(?:,|\.)",
            RegexOptions.CultureInvariant);
    }

    private static bool HasJournalIssue(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return false;
        }

        var afterDate = reference[(dateMatch.Index + dateMatch.Length)..];
        return Regex.IsMatch(afterDate, @"\b\d+\s*\(\s*\d+\s*\)", RegexOptions.CultureInvariant);
    }

    private static bool HasPageRange(string reference)
    {
        return Regex.IsMatch(
            reference,
            @"(?:,\s*|\bpp\.\s*)\d+\s*[-–]\s*\d+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasDoiUrl(string reference)
    {
        return Regex.IsMatch(reference, @"https?://\S+", RegexOptions.CultureInvariant);
    }

    private static bool IsLikelyPreDoiArticle(string reference)
    {
        var year = ExtractReferenceYear(reference);
        return int.TryParse(year, out var parsedYear) && parsedYear < 2000;
    }

    private static bool UsesOldRetrievalPhrase(string reference)
    {
        return reference.Contains("Retrieved from", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Recuperado de", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Recuperado el", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Obtenido de", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnbalancedQuotationMarks(string reference)
    {
        var doubleQuoteCount = reference.Count(character => character == '"');
        var leftQuoteCount = reference.Count(character => character == '“');
        var rightQuoteCount = reference.Count(character => character == '”');

        return doubleQuoteCount % 2 != 0 || leftQuoteCount != rightQuoteCount;
    }

    private static bool HasNoDateWithUnreliableSource(string reference)
    {
        if (!Regex.IsMatch(reference, @"\(\s*(?:n\.?\s*d\.?|s\.?\s*f\.?)\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        return !Regex.IsMatch(reference, @"https?://\S+", RegexOptions.CultureInvariant) &&
            !HasBookPublisher(reference) &&
            !HasWebsiteNameBeforeUrl(reference);
    }

    private static bool HasLikelyCompoundSurname(string authorText)
    {
        var firstAuthor = TextUtilities.NormalizeWhitespace(authorText).Split(',', 2)[0];
        return firstAuthor.Contains('-', StringComparison.Ordinal) ||
            Regex.IsMatch(firstAuthor, @"\b(?:de|del|de la|de los|van|von)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasInitialFirstAuthor(string reference)
    {
        return Regex.IsMatch(
            reference,
            @"^\s*[A-ZÁÉÍÓÚÜÑ]\.\s+[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+",
            RegexOptions.CultureInvariant);
    }

    private static bool HasYearAtEndInsteadOfAfterAuthor(string reference)
    {
        return !Regex.IsMatch(reference, DatePattern, RegexOptions.CultureInvariant) &&
            Regex.IsMatch(reference, @"(?:19|20)\d{2}\.?\s*$", RegexOptions.CultureInvariant);
    }

    private static bool HasManyAuthorsWithoutEllipsis(string authorText)
    {
        var authorCount = Regex.Matches(
            authorText,
            SurnamePattern + @",\s*(?:" + InitialsPattern + @"){1,4}",
            RegexOptions.CultureInvariant).Count;

        return authorCount > 20 &&
            !authorText.Contains("...", StringComparison.Ordinal) &&
            !authorText.Contains("…", StringComparison.Ordinal);
    }

    private static bool HasInstitutionalPublisher(string reference)
    {
        var dateMatch = Regex.Match(reference, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return false;
        }

        var afterDate = reference[(dateMatch.Index + dateMatch.Length)..].Trim();
        var parts = afterDate
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return parts.Count >= 2;
    }

    private static bool HasWebsiteNameBeforeUrl(string reference)
    {
        var urlMatch = Regex.Match(reference, @"https?://\S+", RegexOptions.CultureInvariant);

        if (!urlMatch.Success)
        {
            return false;
        }

        var beforeUrl = reference[..urlMatch.Index].Trim();
        var dateMatch = Regex.Match(beforeUrl, DatePattern, RegexOptions.CultureInvariant);

        if (!dateMatch.Success)
        {
            return false;
        }

        var afterDate = beforeUrl[(dateMatch.Index + dateMatch.Length)..].Trim();
        var parts = afterDate
            .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        return parts.Count >= 2;
    }

    private static bool IsProceedingsSource(string reference)
    {
        return Regex.IsMatch(
            reference,
            @"\bEn\b.{0,180}\b(?:Proceedings|Conference|International Conference|Symposium|Congress|Congreso|Conferencia|Simposio|Actas|Memorias)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsConferencePresentation(string reference)
    {
        return Regex.IsMatch(
            reference,
            @"\[(?:Ponencia|Conference paper|Presentaci[oó]n|Paper presentation)[^\]]*\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(
                reference,
                @"\b(?:congreso|conferencia|conference|symposium|simposio|evento)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !LooksLikeAcademicSource(reference);
    }

    private sealed record BodyPageText(int PageNumber, string Text);

    private sealed record CitationMatch(string Citation, int PageNumber);

    private sealed record HeaderFooterNoiseRemoval(
        string CleanedReference,
        IReadOnlyList<string> RemovedFragments);
}
