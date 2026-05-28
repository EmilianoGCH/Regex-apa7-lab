# Analizador APA 7 para PDFs

Aplicacion local en ASP.NET Core que recibe PDFs, extrae texto seleccionable, separa referencias bibliograficas, revisa cumplimiento APA 7 y genera conteos por año, titulo, autor, editorial/revista y citas.

## Quickstart

### 1. Requisitos

- .NET SDK 10 o compatible con `net10.0`.
- Windows, macOS o Linux con terminal.
- PDFs con texto seleccionable. Los PDFs escaneados como imagen pueden devolver 0 caracteres.

Verifica .NET:

```powershell
dotnet --version
```

### 2. Clonar o abrir el proyecto

Entra a la carpeta del proyecto:

```powershell
cd C:\Users\emili\pyecto-2-progra-mcd-1
```

### 3. Restaurar dependencias

```powershell
dotnet restore
```

El paquete principal para leer PDFs es `UglyToad.PdfPig`.

### 4. Compilar

```powershell
dotnet build
```

Si la aplicacion ya esta corriendo y bloquea el `.exe`, puedes validar en una carpeta aparte:

```powershell
dotnet build -o build-check-current /p:UseAppHost=false
```

### 5. Ejecutar

En PowerShell:

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run
```

Abre la URL que muestre la consola. Normalmente sera:

```text
http://localhost:5000
```

o una URL `https://localhost:...`.

### 6. Usar la aplicacion

1. Abre la pagina en el navegador.
2. Selecciona PDFs individuales o una carpeta con PDFs.
3. Espera a que termine el analisis.
4. Revisa:
   - referencias detectadas,
   - veredicto APA 7,
   - citas parenteticas y narrativas,
   - conteos por año, titulo, autor y editorial/revista,
   - graficas totales.
5. Descarga TXT, reporte HTML o CSV desde los botones de exportacion.

## Salidas generadas

- `ConvertedText/`: guarda los `.txt` extraidos de cada PDF.
- Reporte HTML: se descarga desde el navegador.
- CSV de conteos: `conteos_YYYYMMMDDHHMM.csv`.
- CSV completo: `todas_las_tablas_YYYYMMMDDHHMM.csv`.

## Archivos importantes

- `Program.cs`: configura la app, recibe PDFs y coordina el flujo de analisis.
- `PdfTextService.cs`: extrae texto del PDF con `UglyToad.PdfPig`.
- `Apa7ReferenceService.cs`: limpia, separa, clasifica, valida y cuenta referencias.
- `Models.cs`: define los datos que devuelve el backend.
- `wwwroot/index.html`: interfaz principal.
- `wwwroot/flujo-regex-contadores.htm`: explicacion didactica del flujo de regex y conteos.

## Flujo tecnico resumido

1. El frontend envia PDFs al backend.
2. El backend extrae texto por pagina.
3. Se busca la seccion de referencias.
4. Se limpian encabezados, pies de pagina y ruido.
5. Se separan referencias APA/IEEE aunque vengan mezcladas.
6. Cada referencia se clasifica individualmente.
7. Solo las referencias APA 7 correctas alimentan los conteos estrictos.

## Problemas comunes

- `No se pudo encontrar texto seleccionable`: el PDF probablemente es escaneado o imagen.
- El puerto ya esta ocupado: cierra la app anterior o cambia el puerto con `ASPNETCORE_URLS`.
- Error de restauracion NuGet: revisa conexion a internet o acceso a `https://api.nuget.org`.
- Build bloqueado por `.exe` en uso: detén `dotnet run` y vuelve a compilar, o usa `dotnet build -o build-check-current /p:UseAppHost=false`.
