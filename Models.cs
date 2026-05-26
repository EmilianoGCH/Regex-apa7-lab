public sealed record PdfConversionResult(
    string OriginalFileName,
    string? TextFileName,
    string? DownloadUrl,
    int CharacterCount,
    IReadOnlyList<string> BibliographySections,
    IReadOnlyList<ParentheticalCitation> ParentheticalCitations,
    IReadOnlyDictionary<int, int> YearCounts,
    IReadOnlyDictionary<string, int> AuthorCounts,
    Apa7DocumentAnalysis Apa7Analysis,
    string Message);

public sealed record ParentheticalCitation(
    string Citation,
    int Count);

public sealed record Apa7DocumentAnalysis(
    int TotalReferences,
    int CorrectReferences,
    int IncorrectReferences,
    int OtherFormatReferences,
    IReadOnlyList<ReferenceAnalysis> References);

public sealed record ReferenceAnalysis(
    int Number,
    string Reference,
    string ReferenceType,
    string CitationStyle,
    string AnalysisStatus,
    bool IsApa7Compliant,
    IReadOnlyList<string> Reasons);
