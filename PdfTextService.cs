using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

public static class PdfTextService
{
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

    public static async Task<OcrResult> RunOcrAsync(string pdfPath, string outputPath, string contentRootPath)
    {
        var scriptPath = Path.Combine(contentRootPath, "ocr_pdf.py");

        if (!File.Exists(scriptPath))
        {
            return new OcrResult(false, "No se encontro ocr_pdf.py.");
        }

        using var process = new Process();
        process.StartInfo.FileName = Environment.GetEnvironmentVariable("OCR_PYTHON") ?? "py";
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add(pdfPath);
        process.StartInfo.ArgumentList.Add(outputPath);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.WorkingDirectory = contentRootPath;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stdout.AppendLine(eventArgs.Data);
            Console.WriteLine(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stderr.AppendLine(eventArgs.Data);
            Console.Error.WriteLine(eventArgs.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            return new OcrResult(false, $"No se pudo iniciar Python: {ex.Message}");
        }

        var waitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromMinutes(10)));

        if (completed != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort: the request should return even if the OCR process cannot be killed.
            }

            return new OcrResult(false, "Tiempo maximo de OCR excedido.");
        }

        if (process.ExitCode != 0)
        {
            var details = stderr.Length == 0 ? stdout.ToString() : stderr.ToString();
            return new OcrResult(false, TextUtilities.NormalizeWhitespace(details));
        }

        return new OcrResult(true, TextUtilities.NormalizeWhitespace(stdout.ToString()));
    }
}
