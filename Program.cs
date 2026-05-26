using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

var outputDirectory = Path.Combine(app.Environment.ContentRootPath, "ConvertedText");
Directory.CreateDirectory(outputDirectory);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/pdf/convert", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { message = "La solicitud debe enviarse como multipart/form-data." });
    }

    var form = await request.ReadFormAsync();
    var pdfFiles = form.Files
        .Where(file => Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (pdfFiles.Count == 0)
    {
        return Results.BadRequest(new { message = "Selecciona al menos un archivo PDF." });
    }

    var results = new List<PdfConversionResult>();

    foreach (var file in pdfFiles)
    {
        results.Add(await ConvertPdfAsync(file, outputDirectory));
    }

    return Results.Ok(new { files = results });
});

app.MapGet("/api/pdf/download/{fileName}", (string fileName) =>
{
    var safeFileName = MakeSafeFileName(fileName);
    var path = Path.Combine(outputDirectory, safeFileName);

    return File.Exists(path)
        ? Results.File(path, "text/plain; charset=utf-8", safeFileName)
        : Results.NotFound(new { message = "Archivo TXT no encontrado." });
});

app.Run();

static async Task<PdfConversionResult> ConvertPdfAsync(
    IFormFile file,
    string outputDirectory)
{
    var tempPdfPath = Path.Combine(
        Path.GetTempPath(),
        $"{Path.GetFileNameWithoutExtension(MakeSafeFileName(file.FileName))}-{Guid.NewGuid():N}.pdf");

    var outputFileName = MakeSafeFileName($"{Path.GetFileNameWithoutExtension(file.FileName)}.txt");
    var uniqueOutputFileName = MakeUniqueFileName(outputDirectory, outputFileName);
    var outputPath = Path.Combine(outputDirectory, uniqueOutputFileName);

    try
    {
        await SaveUploadedFileAsync(file, tempPdfPath);

        await using var readStream = File.OpenRead(tempPdfPath);
        var text = PdfTextService.ExtractText(readStream);
        var characterCount = PdfTextService.CountMeaningfulCharacters(text);
        if (characterCount == 0)
        {
            const string noSelectableTextMessage = "No se pudo encontrar texto seleccionable en el documento.";
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {file.FileName}: {noSelectableTextMessage}");

            return new PdfConversionResult(
                file.FileName,
                null,
                null,
                0,
                [],
                [],
                new SortedDictionary<int, int>(),
                new SortedDictionary<string, int>(),
                new Apa7DocumentAnalysis(0, 0, 0, 0, []),
                noSelectableTextMessage);
        }

        await File.WriteAllTextAsync(outputPath, text, Encoding.UTF8);

        var references = Apa7ReferenceService.FindReferences(text);
        var parentheticalCitations = Apa7ReferenceService.FindParentheticalCitations(text);
        var apa7Analysis = Apa7ReferenceService.AnalyzeReferences(references);
        var compliantReferences = apa7Analysis.References
            .Where(reference => reference.IsApa7Compliant)
            .Select(reference => reference.Reference)
            .ToList();

        Apa7ReferenceService.WriteToTerminal(file.FileName, references);

        return new PdfConversionResult(
            file.FileName,
            uniqueOutputFileName,
            $"/api/pdf/download/{Uri.EscapeDataString(uniqueOutputFileName)}",
            characterCount,
            references,
            parentheticalCitations,
            Apa7ReferenceService.CountYears(compliantReferences),
            Apa7ReferenceService.CountFirstAuthors(compliantReferences),
            apa7Analysis,
            "Convertido correctamente");
    }
    catch (Exception ex)
    {
        return new PdfConversionResult(
            file.FileName,
            null,
            null,
            0,
            [],
            [],
            new SortedDictionary<int, int>(),
            new SortedDictionary<string, int>(),
            new Apa7DocumentAnalysis(0, 0, 0, 0, []),
            $"No se pudo convertir: {ex.Message}");
    }
    finally
    {
        if (File.Exists(tempPdfPath))
        {
            File.Delete(tempPdfPath);
        }
    }
}

static async Task SaveUploadedFileAsync(IFormFile file, string destinationPath)
{
    await using var tempFile = File.Create(destinationPath);
    await using var stream = file.OpenReadStream();
    await stream.CopyToAsync(tempFile);
}

static string MakeSafeFileName(string fileName)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var safeName = new string(fileName.Select(character =>
        invalidChars.Contains(character) ? '_' : character).ToArray());

    return string.IsNullOrWhiteSpace(safeName) ? "archivo.txt" : safeName;
}

static string MakeUniqueFileName(string directory, string fileName)
{
    var name = Path.GetFileNameWithoutExtension(fileName);
    var extension = Path.GetExtension(fileName);
    var candidate = fileName;
    var counter = 1;

    while (File.Exists(Path.Combine(directory, candidate)))
    {
        candidate = $"{name}-{counter}{extension}";
        counter++;
    }

    return candidate;
}
