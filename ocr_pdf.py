import argparse
import sys
import tempfile
from pathlib import Path


def fail(message: str) -> int:
    print(message, file=sys.stderr)
    return 1


def main() -> int:
    parser = argparse.ArgumentParser(description="OCR rapido para PDFs escaneados.")
    parser.add_argument("pdf_path", help="Ruta del PDF a procesar.")
    parser.add_argument("output_path", help="Ruta del TXT de salida.")
    args = parser.parse_args()

    pdf_path = Path(args.pdf_path)
    output_path = Path(args.output_path)

    if not pdf_path.exists():
        return fail(f"No existe el PDF: {pdf_path}")

    try:
        import fitz
    except ImportError:
        return fail("Falta PyMuPDF. Instala dependencias con: py -m pip install -r requirements-ocr.txt")

    try:
        import easyocr
    except ImportError:
        return fail("Falta EasyOCR. Instala dependencias con: py -m pip install -r requirements-ocr.txt")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    reader = easyocr.Reader(["es", "en"], gpu=False)
    pages_text = []

    with fitz.open(pdf_path) as document, tempfile.TemporaryDirectory() as temp_dir:
        temp_dir_path = Path(temp_dir)
        total_pages = len(document)

        for page_index, page in enumerate(document, start=1):
            print(f"OCR pagina {page_index}/{total_pages}...", flush=True)
            pixmap = page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
            image_path = temp_dir_path / f"page-{page_index}.png"
            pixmap.save(image_path)

            lines = reader.readtext(str(image_path), detail=0, paragraph=True)
            page_text = "\n".join(line.strip() for line in lines if line.strip())

            pages_text.append(f"--- Pagina {page_index} ---")
            pages_text.append(page_text)
            pages_text.append("")

    output_path.write_text("\n".join(pages_text).strip(), encoding="utf-8")
    print(f"OCR terminado: {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
