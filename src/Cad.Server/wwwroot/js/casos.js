// Búsqueda de casos (HU26), rediseño CAD v2: la fila entera abre el detalle del caso.
// Los enlaces «Detalle» e «Historial» siguen siendo el camino con teclado y lector de pantalla.
(() => {
  document.querySelector("[data-casos] tbody")?.addEventListener("click", (evento) => {
    if (evento.target.closest("a, button") || window.getSelection()?.toString()) return;
    const enlace = evento.target.closest("tr")?.querySelector("[data-abrir-caso]");
    if (enlace) window.location.assign(enlace.href);
  });
})();
