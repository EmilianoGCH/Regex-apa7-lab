using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configura la respuesta JSON para que el frontend reciba nombres camelCase.
// Ejemplo: OriginalFileName en C# sale como originalFileName en JavaScript.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

var outputDirectory = Path.Combine(app.Environment.ContentRootPath, "ConvertedText");
Directory.CreateDirectory(outputDirectory);

// Sirve los archivos estaticos de wwwroot, por ejemplo index.html y los HTM de explicacion.
app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoint que recibe uno o varios PDF y devuelve el analisis.
// Ejemplo de uso desde el navegador: subir C1.pdf y C2.pdf en el formulario.
app.MapPost("/api/pdf/convert", async (HttpRequest request) =>
{
    // El formulario debe venir como multipart/form-data porque trae archivos.
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { message = "La solicitud debe enviarse como multipart/form-data." });
    }

    // Filtra solamente archivos con extension .pdf.
    // Ejemplo: si suben notas.txt y C1.pdf, solo se procesa C1.pdf.
    var form = await request.ReadFormAsync();
    var pdfFiles = form.Files
        .Where(file => Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (pdfFiles.Count == 0)
    {
        return Results.BadRequest(new { message = "Selecciona al menos un archivo PDF." });
    }

    var results = new List<PdfConversionResult>();

    // Procesa cada PDF de forma individual para devolver un resultado por documento.
    foreach (var file in pdfFiles)
    {
        results.Add(await ConvertPdfAsync(file, outputDirectory));
    }

    return Results.Ok(new { files = results });
});

// Endpoint de descarga del TXT extraido.
// Ejemplo: /api/pdf/download/C1.txt devuelve el texto que se obtuvo del PDF.
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
    // Crea un PDF temporal para que PdfPig pueda leerlo como archivo/stream.
    // Ejemplo: C1.pdf se guarda temporalmente como C1-<guid>.pdf.
    var tempPdfPath = Path.Combine(
        Path.GetTempPath(),
        $"{Path.GetFileNameWithoutExtension(MakeSafeFileName(file.FileName))}-{Guid.NewGuid():N}.pdf");

    // Prepara el nombre del TXT de salida evitando caracteres invalidos y duplicados.
    // Ejemplo: C1.pdf produce C1.txt; si ya existe, produce C1-1.txt.
    var outputFileName = MakeSafeFileName($"{Path.GetFileNameWithoutExtension(file.FileName)}.txt");
    var uniqueOutputFileName = MakeUniqueFileName(outputDirectory, outputFileName);
    var outputPath = Path.Combine(outputDirectory, uniqueOutputFileName);

    try
    {
        await SaveUploadedFileAsync(file, tempPdfPath);

        await using var readStream = File.OpenRead(tempPdfPath);
        var text = PdfTextService.ExtractText(readStream);
        var characterCount = PdfTextService.CountMeaningfulCharacters(text);

        // Si el PDF es escaneado o no tiene texto seleccionable, no se puede analizar.
        // Ejemplo: una imagen de una pagina puede tener 0 caracteres utiles.
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
                [],
                new SortedDictionary<int, int>(),
                new SortedDictionary<string, int>(),
                new SortedDictionary<string, int>(),
                new SortedDictionary<string, int>(),
                new Apa7DocumentAnalysis(0, 0, 0, 0, 0, 0, []),
                noSelectableTextMessage);
        }

        await File.WriteAllTextAsync(outputPath, text, Encoding.UTF8);

        // Flujo principal de analisis:
        // 1. separa referencias, 2. valida APA 7, 3. filtra correctas, 4. cuenta y busca citas.
        // Ejemplo: solo las referencias con IsApa7Compliant=true alimentan CountYears.
        var references = Apa7ReferenceService.FindReferences(text);
        var apa7Analysis = Apa7ReferenceService.AnalyzeReferences(references);
        var compliantReferenceAnalyses = apa7Analysis.References
            .Where(reference => reference.IsApa7Compliant)
            .ToList();
        var compliantReferences = compliantReferenceAnalyses
            .Select(reference => reference.Reference)
            .ToList();
        var parentheticalCitations = Apa7ReferenceService.FindTextCitationsByFirstAuthor(text, compliantReferences);
        var narrativeCitations = Apa7ReferenceService.FindNarrativeCitationsByFirstAuthor(text, compliantReferences);

        Apa7ReferenceService.WriteToTerminal(file.FileName, references);

        return new PdfConversionResult(
            file.FileName,
            uniqueOutputFileName,
            $"/api/pdf/download/{Uri.EscapeDataString(uniqueOutputFileName)}",
            characterCount,
            references,
            parentheticalCitations,
            narrativeCitations,
            Apa7ReferenceService.CountYears(compliantReferences),
            Apa7ReferenceService.CountTitles(compliantReferences),
            Apa7ReferenceService.CountFirstAuthors(compliantReferences),
            Apa7ReferenceService.CountPublishers(compliantReferenceAnalyses),
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
            [],
            new SortedDictionary<int, int>(),
            new SortedDictionary<string, int>(),
            new SortedDictionary<string, int>(),
            new SortedDictionary<string, int>(),
            new Apa7DocumentAnalysis(0, 0, 0, 0, 0, 0, []),
            $"No se pudo convertir: {ex.Message}");
    }
    finally
    {
        // Limpia el archivo temporal aunque el analisis falle.
        if (File.Exists(tempPdfPath))
        {
            File.Delete(tempPdfPath);
        }
    }
}

// Copia el PDF subido al archivo temporal.
// Ejemplo: el stream que llega del navegador se guarda en tempPdfPath.
static async Task SaveUploadedFileAsync(IFormFile file, string destinationPath)
{
    await using var tempFile = File.Create(destinationPath);
    await using var stream = file.OpenReadStream();
    await stream.CopyToAsync(tempFile);
}

// Reemplaza caracteres invalidos para que el nombre sea seguro en Windows.
// Ejemplo: "mi:archivo.pdf" se convierte en "mi_archivo.pdf".
static string MakeSafeFileName(string fileName)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var safeName = new string(fileName.Select(character =>
        invalidChars.Contains(character) ? '_' : character).ToArray());

    return string.IsNullOrWhiteSpace(safeName) ? "archivo.txt" : safeName;
}

// Evita sobrescribir archivos anteriores agregando un contador.
// Ejemplo: si C1.txt existe, intenta C1-1.txt, luego C1-2.txt, etc.
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
