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

## Uso

1. Selecciona PDFs individuales o una carpeta con PDFs.
2. El frontend envia los archivos al endpoint `POST /api/pdf/convert`.
3. El backend extrae texto con `UglyToad.PdfPig`.
4. Si no hay texto seleccionable, informa que no se pudo encontrar texto seleccionable en el documento.
5. Cuando si hay texto, los TXT quedan en la carpeta `ConvertedText`.
