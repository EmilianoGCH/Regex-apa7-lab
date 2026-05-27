const fileInput = document.querySelector("#fileInput");
const folderInput = document.querySelector("#folderInput");
const dropZone = document.querySelector("#dropZone");
const statusText = document.querySelector("#status");
const resultsList = document.querySelector("#resultsList");

fileInput.addEventListener("change", () => uploadFiles(fileInput.files));
folderInput.addEventListener("change", () => uploadFiles(folderInput.files));

dropZone.addEventListener("dragover", (event) => {
  event.preventDefault();
  dropZone.classList.add("is-over");
});

dropZone.addEventListener("dragleave", () => {
  dropZone.classList.remove("is-over");
});

dropZone.addEventListener("drop", (event) => {
  event.preventDefault();
  dropZone.classList.remove("is-over");
  uploadFiles(event.dataTransfer.files);
});

async function uploadFiles(fileList) {
  const files = Array.from(fileList).filter((file) =>
    file.name.toLowerCase().endsWith(".pdf")
  );

  if (files.length === 0) {
    statusText.textContent = "Selecciona al menos un PDF.";
    return;
  }

  const formData = new FormData();
  files.forEach((file) => formData.append("files", file));

  statusText.textContent = `Convirtiendo ${files.length} archivo(s)...`;
  resultsList.innerHTML = "";

  try {
    const response = await fetch("/api/pdf/convert", {
      method: "POST",
      body: formData,
    });

    const payload = await response.json();

    if (!response.ok) {
      throw new Error(payload.message || "No se pudo completar la conversion.");
    }

    renderResults(payload.files);
    statusText.textContent = `Proceso terminado: ${payload.files.length} archivo(s) revisado(s).`;
  } catch (error) {
    statusText.textContent = error.message;
  } finally {
    fileInput.value = "";
    folderInput.value = "";
  }
}

function renderResults(files) {
  if (!files.length) {
    resultsList.innerHTML = '<p class="empty">No hay resultados.</p>';
    return;
  }

  resultsList.innerHTML = "";

  files.forEach((file) => {
    const item = document.createElement("details");
    item.className = "result-item";

    const summary = document.createElement("summary");
    summary.className = "result-summary";

    const summaryTitle = document.createElement("span");
    summaryTitle.textContent = file.originalFileName;

    const summaryCount = document.createElement("span");
    summaryCount.className = "result-count";
    summaryCount.textContent = `${file.bibliographySections.length} coincidencia(s)`;

    summary.append(summaryTitle, summaryCount);
    item.append(summary);

    const body = document.createElement("div");
    body.className = "result-body";

    const meta = document.createElement("div");
    meta.className = "result-meta";

    const message = document.createElement("p");
    message.textContent = `${file.message} Caracteres extraidos: ${file.characterCount}.`;

    meta.append(message);
    meta.append(createParentheticalCitationsBlock(file.parentheticalCitations || []));
    meta.append(createNarrativeCitationsBlock(file.narrativeCitations || []));
    meta.append(createApa7AnalysisBlock(file.apa7Analysis));

    const bibliography = document.createElement("div");
    bibliography.className = "bibliography";

    if (file.bibliographySections.length > 0) {
      const bibliographyTitle = document.createElement("h4");
      bibliographyTitle.textContent = "Referencias APA 7 encontradas";
      bibliography.append(bibliographyTitle);

      file.bibliographySections.forEach((section, index) => {
        const block = document.createElement("pre");
        block.textContent = `Coincidencia ${index + 1}\n${section}`;
        bibliography.append(block);
      });
    } else {
      const empty = document.createElement("p");
      empty.className = "bibliography-empty";
      empty.textContent = "No se encontro una seccion de referencias bibliograficas o bibliografia.";
      bibliography.append(empty);
    }

    meta.append(bibliography);
    body.append(meta);

    if (file.downloadUrl) {
      const link = document.createElement("a");
      link.className = "download";
      link.href = file.downloadUrl;
      link.textContent = "Descargar TXT";
      body.append(link);
    }

    item.append(body);
    resultsList.append(item);
  });

  renderYearSummary(files);
  renderAuthorSummary(files);
  renderPublisherSummary(files);
}

function createParentheticalCitationsBlock(citations) {
  const container = document.createElement("div");
  container.className = "bibliography";

  const title = document.createElement("h4");
  title.textContent = `Citas en el texto por primer autor APA 7 (${citations.length})`;
  container.append(title);

  if (citations.length === 0) {
    const empty = document.createElement("p");
    empty.className = "bibliography-empty";
    empty.textContent = "No se detectaron menciones de primeros autores APA 7 correctos en el cuerpo del documento.";
    container.append(empty);
    return container;
  }

  citations.forEach((citation) => {
    const block = document.createElement("pre");
    block.textContent = `${citation.citation} - ${citation.count} vez/veces - ${formatPages(citation.pages || [])}`;
    container.append(block);
  });

  return container;
}

function createNarrativeCitationsBlock(citations) {
  const container = document.createElement("div");
  container.className = "bibliography";

  const title = document.createElement("h4");
  title.textContent = `Citas narrativas por primer autor APA 7 (${citations.length})`;
  container.append(title);

  if (citations.length === 0) {
    const empty = document.createElement("p");
    empty.className = "bibliography-empty";
    empty.textContent = "No se detectaron citas narrativas con primeros autores de referencias APA 7 correctas.";
    container.append(empty);
    return container;
  }

  citations.forEach((citation) => {
    const block = document.createElement("pre");
    block.textContent = `${citation.citation} - ${citation.count} vez/veces - ${formatPages(citation.pages || [])}`;
    container.append(block);
  });

  return container;
}

function formatPages(pages) {
  const uniquePages = Array.from(new Set((pages || [])
    .map((page) => Number(page))
    .filter((page) => Number.isFinite(page) && page > 0)))
    .sort((a, b) => a - b);

  if (uniquePages.length === 0) {
    return "p. no disponible";
  }

  return uniquePages.length === 1
    ? `p. ${uniquePages[0]}`
    : `pp. ${uniquePages.join(", ")}`;
}

function renderYearSummary(files) {
  const years = Array.from(
    new Set(
      files.flatMap((file) =>
        Object.keys(file.yearCounts || {}).map((year) => Number(year))
      )
    )
  ).sort((a, b) => a - b);

  const summary = document.createElement("details");
  summary.className = "year-summary";

  const summaryHeader = document.createElement("summary");
  summaryHeader.className = "result-summary";

  const title = document.createElement("h3");
  title.textContent = "Años citados en APA 7 correctas";

  const count = document.createElement("span");
  count.className = "result-count";
  count.textContent = `${years.length} año(s)`;

  summaryHeader.append(title, count);
  summary.append(summaryHeader);

  const body = document.createElement("div");
  body.className = "result-body";

  if (years.length === 0) {
    const empty = document.createElement("p");
    empty.className = "empty";
    empty.textContent = "No se detectaron años en referencias APA 7 correctas.";
    body.append(empty);
    summary.append(body);
    resultsList.append(summary);
    return;
  }

  const tableWrap = document.createElement("div");
  tableWrap.className = "table-wrap";

  const table = document.createElement("table");
  table.className = "year-table";

  const thead = document.createElement("thead");
  const headerRow = document.createElement("tr");

  const yearHeader = document.createElement("th");
  yearHeader.textContent = "Año";
  headerRow.append(yearHeader);

  files.forEach((file) => {
    const header = document.createElement("th");
    header.textContent = file.originalFileName;
    headerRow.append(header);
  });

  const totalHeader = document.createElement("th");
  totalHeader.textContent = "Total";
  headerRow.append(totalHeader);

  thead.append(headerRow);
  table.append(thead);

  const tbody = document.createElement("tbody");

  years.forEach((year) => {
    const row = document.createElement("tr");
    const yearCell = document.createElement("th");
    yearCell.scope = "row";
    yearCell.textContent = String(year);
    row.append(yearCell);

    let rowTotal = 0;

    files.forEach((file) => {
      const value = (file.yearCounts || {})[year] || 0;
      rowTotal += value;

      const cell = document.createElement("td");
      cell.textContent = String(value);
      row.append(cell);
    });

    const totalCell = document.createElement("td");
    totalCell.textContent = String(rowTotal);
    row.append(totalCell);

    tbody.append(row);
  });

  table.append(tbody);

  const tfoot = document.createElement("tfoot");
  const totalRow = document.createElement("tr");
  const totalLabel = document.createElement("th");
  totalLabel.scope = "row";
  totalLabel.textContent = "Total";
  totalRow.append(totalLabel);

  let grandTotal = 0;

  files.forEach((file) => {
    const fileTotal = Object.values(file.yearCounts || {})
      .reduce((sum, value) => sum + Number(value || 0), 0);
    grandTotal += fileTotal;

    const cell = document.createElement("td");
    cell.textContent = String(fileTotal);
    totalRow.append(cell);
  });

  const grandTotalCell = document.createElement("td");
  grandTotalCell.textContent = String(grandTotal);
  totalRow.append(grandTotalCell);
  tfoot.append(totalRow);
  table.append(tfoot);

  tableWrap.append(table);
  body.append(tableWrap);
  summary.append(body);
  resultsList.append(summary);
}

function createApa7AnalysisBlock(analysis) {
  const container = document.createElement("div");
  container.className = "apa-analysis";

  const title = document.createElement("h4");
  title.textContent = "Análisis APA 7";
  container.append(title);

  if (!analysis || analysis.totalReferences === 0) {
    const empty = document.createElement("p");
    empty.className = "bibliography-empty";
    empty.textContent = "No hay referencias para analizar.";
    container.append(empty);
    return container;
  }

  const summary = document.createElement("div");
  summary.className = "analysis-metrics";
  summary.append(
    createMetric("Total", analysis.totalReferences),
    createMetric("Correctas", analysis.correctReferences, "ok"),
    createMetric("APA con errores", analysis.incorrectReferences, "bad"),
    createMetric("No cumple APA 7", analysis.nonCompliantReferences || 0, "bad"),
    createMetric("Otro formato", analysis.otherFormatReferences || 0),
    createMetric("Verificar manualmente", analysis.manualReviewReferences || 0)
  );
  container.append(summary);

  const filters = document.createElement("div");
  filters.className = "analysis-filters";
  filters.append(
    createFilterButton("Todas", analysis.references.length, "is-active"),
    createFilterButton("Sí cumplen", analysis.correctReferences, "ok"),
    createFilterButton("APA con errores", analysis.incorrectReferences, "bad"),
    createFilterButton("No cumple APA 7", analysis.nonCompliantReferences || 0, "bad"),
    createFilterButton("Otro formato", analysis.otherFormatReferences || 0)
  );
  container.append(filters);

  const list = document.createElement("div");
  list.className = "analysis-list";

  analysis.references.forEach((reference) => {
    const item = document.createElement("details");
    item.className = "analysis-item";

    const header = document.createElement("summary");
    header.className = "analysis-header";

    const label = document.createElement("span");
    label.className = "analysis-label";

    const number = document.createElement("span");
    number.className = "analysis-number";
    number.textContent = `#${reference.number}`;

    const type = document.createElement("span");
    type.className = "analysis-type";
    type.textContent = reference.referenceType;

    label.append(number, type);

    const status = document.createElement("span");
    status.className = reference.isApa7Compliant ? "status-ok" : "status-bad";
    status.textContent = reference.isApa7Compliant ? "Sí cumple" : "No cumple";

    header.append(label, status);
    item.append(header);

    const content = document.createElement("div");
    content.className = "analysis-content";

    const referenceText = document.createElement("pre");
    referenceText.textContent = reference.reference;
    content.append(referenceText);

    const reasons = document.createElement("ul");
    reasons.className = "analysis-reasons";
    reference.reasons.forEach((reason) => {
      const reasonItem = document.createElement("li");
      reasonItem.textContent = reason.startsWith("No cumple") || reason.startsWith("Advertencia")
        ? `⚠️ ${reason}`
        : reason;
      reasons.append(reasonItem);
    });

    content.append(reasons);
    item.append(content);
    list.append(item);
  });

  container.append(list);
  return container;
}

function createMetric(label, value, tone = "") {
  const metric = document.createElement("div");
  metric.className = `analysis-metric ${tone}`.trim();

  const number = document.createElement("strong");
  number.textContent = String(value);

  const text = document.createElement("span");
  text.textContent = label;

  metric.append(number, text);
  return metric;
}

function createFilterButton(label, value, tone = "") {
  const button = document.createElement("button");
  button.className = `analysis-filter ${tone}`.trim();
  button.type = "button";
  button.textContent = `${label} (${value})`;
  return button;
}

function renderAuthorSummary(files) {
  const authors = Array.from(
    new Set(
      files.flatMap((file) => Object.keys(file.authorCounts || {}))
    )
  ).sort((a, b) => a.localeCompare(b, "es"));

  const summary = document.createElement("details");
  summary.className = "year-summary";

  const summaryHeader = document.createElement("summary");
  summaryHeader.className = "result-summary";

  const title = document.createElement("h3");
  title.textContent = "Primeros autores en APA 7 correctas";

  const count = document.createElement("span");
  count.className = "result-count";
  count.textContent = `${authors.length} autor(es)`;

  summaryHeader.append(title, count);
  summary.append(summaryHeader);

  const body = document.createElement("div");
  body.className = "result-body";

  if (authors.length === 0) {
    const empty = document.createElement("p");
    empty.className = "empty";
    empty.textContent = "No se detectaron primeros autores en referencias APA 7 correctas.";
    body.append(empty);
    summary.append(body);
    resultsList.append(summary);
    return;
  }

  const tableWrap = document.createElement("div");
  tableWrap.className = "table-wrap";

  const table = document.createElement("table");
  table.className = "year-table";

  const thead = document.createElement("thead");
  const headerRow = document.createElement("tr");

  const authorHeader = document.createElement("th");
  authorHeader.textContent = "Primer autor";
  headerRow.append(authorHeader);

  files.forEach((file) => {
    const header = document.createElement("th");
    header.textContent = file.originalFileName;
    headerRow.append(header);
  });

  const totalHeader = document.createElement("th");
  totalHeader.textContent = "Total";
  headerRow.append(totalHeader);

  thead.append(headerRow);
  table.append(thead);

  const tbody = document.createElement("tbody");

  authors.forEach((author) => {
    const row = document.createElement("tr");
    const authorCell = document.createElement("th");
    authorCell.scope = "row";
    authorCell.textContent = author;
    row.append(authorCell);

    let rowTotal = 0;

    files.forEach((file) => {
      const value = (file.authorCounts || {})[author] || 0;
      rowTotal += value;

      const cell = document.createElement("td");
      cell.textContent = String(value);
      row.append(cell);
    });

    const totalCell = document.createElement("td");
    totalCell.textContent = String(rowTotal);
    row.append(totalCell);

    tbody.append(row);
  });

  table.append(tbody);

  const tfoot = document.createElement("tfoot");
  const totalRow = document.createElement("tr");
  const totalLabel = document.createElement("th");
  totalLabel.scope = "row";
  totalLabel.textContent = "Total";
  totalRow.append(totalLabel);

  let grandTotal = 0;

  files.forEach((file) => {
    const fileTotal = Object.values(file.authorCounts || {})
      .reduce((sum, value) => sum + Number(value || 0), 0);
    grandTotal += fileTotal;

    const cell = document.createElement("td");
    cell.textContent = String(fileTotal);
    totalRow.append(cell);
  });

  const grandTotalCell = document.createElement("td");
  grandTotalCell.textContent = String(grandTotal);
  totalRow.append(grandTotalCell);
  tfoot.append(totalRow);
  table.append(tfoot);

  tableWrap.append(table);
  body.append(tableWrap);
  summary.append(body);
  resultsList.append(summary);
}

function renderPublisherSummary(files) {
  const publishers = Array.from(
    new Set(
      files.flatMap((file) => Object.keys(file.publisherCounts || {}))
    )
  ).sort((a, b) => a.localeCompare(b, "es"));

  const summary = document.createElement("details");
  summary.className = "year-summary";

  const summaryHeader = document.createElement("summary");
  summaryHeader.className = "result-summary";

  const title = document.createElement("h3");
  title.textContent = "Editorial o revista en APA 7 correctas";

  const count = document.createElement("span");
  count.className = "result-count";
  count.textContent = `${publishers.length} fuente(s)`;

  summaryHeader.append(title, count);
  summary.append(summaryHeader);

  const body = document.createElement("div");
  body.className = "result-body";

  if (publishers.length === 0) {
    const empty = document.createElement("p");
    empty.className = "empty";
    empty.textContent = "No se detectaron editoriales o revistas en referencias APA 7 correctas.";
    body.append(empty);
    summary.append(body);
    resultsList.append(summary);
    return;
  }

  const tableWrap = document.createElement("div");
  tableWrap.className = "table-wrap";

  const table = document.createElement("table");
  table.className = "year-table";

  const thead = document.createElement("thead");
  const headerRow = document.createElement("tr");

  const publisherHeader = document.createElement("th");
  publisherHeader.textContent = "Editorial / revista";
  headerRow.append(publisherHeader);

  files.forEach((file) => {
    const header = document.createElement("th");
    header.textContent = file.originalFileName;
    headerRow.append(header);
  });

  const totalHeader = document.createElement("th");
  totalHeader.textContent = "Total";
  headerRow.append(totalHeader);

  thead.append(headerRow);
  table.append(thead);

  const tbody = document.createElement("tbody");

  publishers.forEach((publisher) => {
    const row = document.createElement("tr");
    const publisherCell = document.createElement("th");
    publisherCell.scope = "row";
    publisherCell.textContent = publisher;
    row.append(publisherCell);

    let rowTotal = 0;

    files.forEach((file) => {
      const value = (file.publisherCounts || {})[publisher] || 0;
      rowTotal += value;

      const cell = document.createElement("td");
      cell.textContent = String(value);
      row.append(cell);
    });

    const totalCell = document.createElement("td");
    totalCell.textContent = String(rowTotal);
    row.append(totalCell);

    tbody.append(row);
  });

  table.append(tbody);

  const tfoot = document.createElement("tfoot");
  const totalRow = document.createElement("tr");
  const totalLabel = document.createElement("th");
  totalLabel.scope = "row";
  totalLabel.textContent = "Total";
  totalRow.append(totalLabel);

  let grandTotal = 0;

  files.forEach((file) => {
    const fileTotal = Object.values(file.publisherCounts || {})
      .reduce((sum, value) => sum + Number(value || 0), 0);
    grandTotal += fileTotal;

    const cell = document.createElement("td");
    cell.textContent = String(fileTotal);
    totalRow.append(cell);
  });

  const grandTotalCell = document.createElement("td");
  grandTotalCell.textContent = String(grandTotal);
  totalRow.append(grandTotalCell);
  tfoot.append(totalRow);
  table.append(tfoot);

  tableWrap.append(table);
  body.append(tableWrap);
  summary.append(body);
  resultsList.append(summary);
}
