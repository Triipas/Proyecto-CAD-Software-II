// Búsqueda de casos (HU26) y bandeja de reportes, rediseño CAD v2: la fila entera abre el caso.
// Los enlaces de cada fila siguen siendo el camino con teclado y lector de pantalla.
(() => {
  for (const cuerpo of document.querySelectorAll("[data-casos] tbody, [data-reportes] tbody")) {
    cuerpo.addEventListener("click", (evento) => {
      if (evento.target.closest("a, button") || window.getSelection()?.toString()) return;
      const enlace = evento.target.closest("tr")?.querySelector("[data-abrir-caso]");
      if (enlace) window.location.assign(enlace.href);
    });
  }
})();
