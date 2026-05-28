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

public sealed record ParentheticalCitation(
    string Citation,
    int Count,
    IReadOnlyList<int> Pages);

public sealed record Apa7DocumentAnalysis(
    int TotalReferences,
    int CorrectReferences,
    int IncorrectReferences,
    int NonCompliantReferences,
    int OtherFormatReferences,
    int ManualReviewReferences,
    IReadOnlyList<ReferenceAnalysis> References);

public sealed record ReferenceAnalysis(
    int Number,
    string Reference,
    string ReferenceType,
    string CitationStyle,
    string AnalysisStatus,
    bool IsApa7Compliant,
    IReadOnlyList<string> Reasons);
