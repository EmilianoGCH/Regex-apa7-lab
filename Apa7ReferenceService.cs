using System.Text.RegularExpressions;

public static class Apa7ReferenceService
{
    private const string DatePattern = @"\(\s*(?:n\.?\s*d\.?|s\.?\s*f\.?|(?:\d{1,2}\s+de\s+[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+\s+de\s+)?(?:19|20)\d{2}[a-z]?)(?:,\s*[^)]{1,40})?\s*\)";
    private const string InitialsPattern = @"(?:[A-ZÁÉÍÓÚÜÑ][a-záéíóúüñ]{0,2}\.?\s*(?:de\s+|del\s+|de la\s+)?)";
    private const string SurnamePattern = @"[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+(?:\s+[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+){0,2}";
    private const string PersonAuthorPattern = SurnamePattern + @",\s*(?:" + InitialsPattern + @"){1,4}(?:,\s*&\s*|,\s*|;\s*&?\s*|&\s*|,\s*y\s*|\s+y\s+)?";
    private const string GroupAuthorPattern = @"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9&'\-]{2,}(?:\s+(?:de|del|la|las|los|el|en|y|e|of|the|and|for|[A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9&'\-]{2,})){0,14}\.\s*(?:\([^)]{2,40}\)\.\s*)?";

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

    public static Apa7DocumentAnalysis AnalyzeReferences(IReadOnlyList<string> references)
    {
        var analyzedReferences = references
            .Select((reference, index) => AnalyzeReference(index + 1, reference))
            .ToList();

        return new Apa7DocumentAnalysis(
            analyzedReferences.Count,
            analyzedReferences.Count(reference => reference.IsApa7Compliant),
            analyzedReferences.Count(reference => reference.AnalysisStatus == "Apa7WithErrors"),
            analyzedReferences.Count(reference => reference.AnalysisStatus == "OtherFormat"),
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

        var sectionLines = new List<string>();

        for (var index = startIndex; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.Length == 0 ||
                Regex.IsMatch(line, @"^---\s*Pagina\s+\d+\s*---$", RegexOptions.CultureInvariant) ||
                Regex.IsMatch(line, @"^\d+$", RegexOptions.CultureInvariant))
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

    private static string RemoveReferenceSectionTitle(string section)
    {
        return Regex.Replace(
            section,
            @"(?is)^\s*(?:[IVXLCDM]+|\d+)?[\.\)]?\s*(?:referencias\s+bibliogr[aá]ficas|referencias|bibliograf[ií]a|references)\b\s*",
            string.Empty,
            RegexOptions.CultureInvariant).Trim();
    }

    private static List<string> SplitReferences(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return [];
        }

        var text = TextUtilities.NormalizeWhitespace(RemoveReferenceSectionTitle(section));
        var startMatches = Regex.Matches(
            text,
            $@"(?<!\p{{L}})(?<entryStart>(?:{PersonAuthorPattern}(?:(?:{PersonAuthorPattern}){{0,19}})|{GroupAuthorPattern}){DatePattern})",
            RegexOptions.CultureInvariant);

        var starts = startMatches
            .Cast<Match>()
            .Select(match => match.Index)
            .Where(index => IsLikelyReferenceStart(text, index))
            .Distinct()
            .Order()
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
        return Regex.Replace(
            normalized,
            @"^\d+\s+(?=[A-ZÁÉÍÓÚÑ])",
            string.Empty,
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

    private static ReferenceAnalysis AnalyzeReference(int number, string reference)
    {
        var normalized = TextUtilities.NormalizeWhitespace(reference);
        var reasons = new List<string>();
        var citationStyle = DetectCitationStyle(normalized);
        var referenceType = citationStyle == "APA 7" ? ClassifyReference(normalized) : "Otro formato";
        var isNonStandardApaType = false;

        if (citationStyle != "APA 7")
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
            .Where(reason => reason.StartsWith("No cumple:", StringComparison.Ordinal))
            .ToList();

        if (blockingReasons.Count == 0)
        {
            reasons.Insert(0, "Cumple estructura APA 7 basica para el tipo detectado.");
        }

        return new ReferenceAnalysis(
            number,
            normalized,
            referenceType,
            citationStyle,
            blockingReasons.Count == 0 && !isNonStandardApaType ? "Apa7Correct" : "Apa7WithErrors",
            blockingReasons.Count == 0,
            reasons);
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

    private static bool IsIeeeReference(string reference)
    {
        return Regex.IsMatch(reference, @"^\s*\[\d+\]", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(reference, @"^[A-Z]\.\s*[A-ZÁÉÍÓÚÑ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ'\-]+.*,\s*[""“].+[""”].*,\s*(?:19|20)\d{2}\.?\s*$", RegexOptions.CultureInvariant);
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
                if (!HasJournalVolume(reference))
                {
                    reasons.Add("No cumple: articulo de revista sin volumen detectable.");
                }

                if (!Regex.IsMatch(reference, @"https?://\S+", RegexOptions.CultureInvariant))
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
                if (!Regex.IsMatch(reference, @"\bEn\s+", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: capitulo de libro/actas sin 'En' antes del libro o proceedings.");
                }

                if (!Regex.IsMatch(reference, @"\b\(Eds?\.?\)|\b\(Ed\.?\)", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: capitulo de libro/actas sin editor en formato (Ed.) o (Eds.).");
                }

                if (!Regex.IsMatch(reference, @"\(pp\.\s*\d+\s*[-–]\s*\d+\)", RegexOptions.CultureInvariant))
                {
                    reasons.Add("No cumple: capitulo de libro/actas sin paginas en formato (pp. xx-xx).");
                }

                if (!HasBookPublisher(reference))
                {
                    reasons.Add("No cumple: capitulo de libro/actas sin editorial identificable.");
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
                reasons.Add("No cumple: tipo ambiguo; no se pueden verificar campos obligatorios de una fuente APA 7 concreta.");
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

    private static bool UsesOldRetrievalPhrase(string reference)
    {
        return reference.Contains("Retrieved from", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Recuperado de", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains("Obtenido de", StringComparison.OrdinalIgnoreCase);
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
            @"\b(?:Proceedings|Conference|International Conference|Symposium|Congress|Congreso|Conferencia|Simposio|Actas|Memorias)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
