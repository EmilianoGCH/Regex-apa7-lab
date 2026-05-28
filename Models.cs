// Resultado completo que el backend devuelve por cada PDF procesado.
// Ejemplo: C1.pdf puede devolver referencias, citas, conteos por año y el analisis APA 7.
public sealed record PdfConversionResult(
    string OriginalFileName,
    string? TextFileName,
    string? DownloadUrl,
    int CharacterCount,
    IReadOnlyList<string> BibliographySections,
    IReadOnlyList<ParentheticalCitation> ParentheticalCitations,
    IReadOnlyList<ParentheticalCitation> NarrativeCitations,
    IReadOnlyDictionary<int, int> YearCounts,
    IReadOnlyDictionary<string, int> TitleCounts,
    IReadOnlyDictionary<string, int> AuthorCounts,
    IReadOnlyDictionary<string, int> PublisherCounts,
    Apa7DocumentAnalysis Apa7Analysis,
    string Message);

// Representa una cita encontrada en el cuerpo del documento.
// Ejemplo: Citation="Garcia", Count=3, Pages=[2, 5].
public sealed record ParentheticalCitation(
    string Citation,
    int Count,
    IReadOnlyList<int> Pages);

// Resumen numerico de la revision bibliografica de un documento.
// Ejemplo: TotalReferences=10, CorrectReferences=6, OtherFormatReferences=2.
public sealed record Apa7DocumentAnalysis(
    int TotalReferences,
    int CorrectReferences,
    int IncorrectReferences,
    int NonCompliantReferences,
    int OtherFormatReferences,
    int ManualReviewReferences,
    IReadOnlyList<ReferenceAnalysis> References);

// Resultado de analizar una sola referencia.
// Ejemplo: ReferenceType="Articulo de revista", AnalysisStatus="Apa7Correct".
public sealed record ReferenceAnalysis(
    int Number,
    string Reference,
    string ReferenceType,
    string CitationStyle,
    string AnalysisStatus,
    bool IsApa7Compliant,
    IReadOnlyList<string> Reasons);
