// Bandeja de reportes, rediseño CAD v2: la fila entera abre el caso. (La búsqueda de casos tiene su propio script, casos-vista.js.)
// Los enlaces de cada fila siguen siendo el camino con teclado y lector de pantalla.
(() => {
  for (const cuerpo of document.querySelectorAll("[data-reportes] tbody")) {
    cuerpo.addEventListener("click", (evento) => {
      if (evento.target.closest("a, button") || window.getSelection()?.toString()) return;
      const enlace = evento.target.closest("tr")?.querySelector("[data-abrir-caso]");
      if (enlace) window.location.assign(enlace.href);
    });
  }
})();
