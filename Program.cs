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
        results.Add(await ConvertPdfAsync(file, app.Environment.ContentRootPath, outputDirectory));
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
    string contentRootPath,
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
        var message = "Convertido correctamente";

        if (characterCount == 0)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {file.FileName}: sin texto seleccionable, iniciando OCR...");
            var ocrResult = await PdfTextService.RunOcrAsync(tempPdfPath, outputPath, contentRootPath);

            if (ocrResult.Success)
            {
                text = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
                characterCount = PdfTextService.CountMeaningfulCharacters(text);
                message = characterCount == 0
                    ? "OCR terminado, pero no se detecto texto legible."
                    : "Convertido con OCR porque el PDF parece escaneado.";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {file.FileName}: OCR terminado con {characterCount} caracteres.");
            }
            else
            {
                message = $"Este PDF parece escaneado y requiere OCR, pero fallo el OCR: {ocrResult.Message}";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {file.FileName}: fallo OCR. {ocrResult.Message}");
            }
        }

        if (!File.Exists(outputPath))
        {
            await File.WriteAllTextAsync(outputPath, text, Encoding.UTF8);
        }

        var references = Apa7ReferenceService.FindReferences(text);
        var apa7Analysis = Apa7ReferenceService.AnalyzeReferences(references);
        Apa7ReferenceService.WriteToTerminal(file.FileName, references);

        return new PdfConversionResult(
            file.FileName,
            uniqueOutputFileName,
            $"/api/pdf/download/{Uri.EscapeDataString(uniqueOutputFileName)}",
            characterCount,
            references,
            Apa7ReferenceService.CountYears(references),
            Apa7ReferenceService.CountFirstAuthors(references),
            apa7Analysis,
            message);
    }
    catch (Exception ex)
    {
        return new PdfConversionResult(
            file.FileName,
            null,
            null,
            0,
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
