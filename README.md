# PDF a TXT

Proyecto base en ASP.NET Core con frontend sencillo en HTML, CSS y JavaScript.

## Decision tecnica

La conversion de PDF a TXT se hace en el backend con C# porque:

- El navegador solo debe seleccionar o subir archivos.
- La extraccion de texto de PDF necesita una libreria especializada.
- El backend puede guardar los `.txt` generados y exponer enlaces de descarga.

## Ejecutar

```powershell
$env:DOTNET_CLI_HOME='C:\Users\emili\pyecto-2-progra-mcd-1\.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet run
```

Abre la URL que muestre la consola. Normalmente sera `http://localhost:5000` o una URL similar.

## OCR rapido para PDFs escaneados

El backend primero intenta extraer texto directo con C#. Si el PDF no trae texto seleccionable, ejecuta `ocr_pdf.py` para aplicar OCR y generar el TXT.

Instala las dependencias una vez:

```powershell
py -m pip install -r requirements-ocr.txt
```

Tambien puedes probar el OCR directo con:

```powershell
py ocr_pdf.py pdf\C10.pdf ConvertedText\C10-ocr.txt
```

## Uso

1. Selecciona PDFs individuales o una carpeta con PDFs.
2. El frontend envia los archivos al endpoint `POST /api/pdf/convert`.
3. El backend extrae texto con `UglyToad.PdfPig`.
4. Si no hay texto seleccionable, ejecuta OCR con Python, PyMuPDF y EasyOCR.
5. Los TXT quedan en la carpeta `ConvertedText`.

Nota: la primera ejecucion de EasyOCR puede tardar porque descarga modelos de reconocimiento.

Si tienes varios Python instalados, puedes indicar cual usara el backend:

```powershell
$env:OCR_PYTHON='C:\Ruta\A\python.exe'
```
