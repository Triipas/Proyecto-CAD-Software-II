// Asignación en el detalle de un SOS o de un reporte (29/10/2026): filtro por nombre de la lista «Elegir a otro sereno».
// Sin JavaScript la lista se ve completa y se elige igual; el filtro solo ayuda cuando crece.
// 05/11/2026: con delegación de eventos, porque el bloque se rehace en vivo cuando un sereno se mueve (consola.js).
(() => {
  const sinTildes = (texto) => texto.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();

  document.addEventListener("input", (evento) => {
    const buscar = evento.target.closest?.("[data-asignar-buscar]");
    const bloque = buscar?.closest("[data-asignar-caso]");
    if (!bloque) return;
    const consulta = sinTildes(buscar.value.trim());
    let visibles = 0;
    for (const opcion of bloque.querySelectorAll("[data-asignar-opcion]")) {
      const coincide = sinTildes(opcion.dataset.nombre).includes(consulta);
      opcion.hidden = !coincide;
      if (coincide) visibles++;
    }
    const sinResultados = bloque.querySelector("[data-asignar-sin-resultados]");
    if (sinResultados) sinResultados.hidden = visibles > 0;
  });

  // Intro en el buscador no envía el formulario sin sereno elegido: elige al primero visible.
  document.addEventListener("keydown", (evento) => {
    const buscar = evento.target.closest?.("[data-asignar-buscar]");
    if (!buscar || evento.key !== "Enter") return;
    evento.preventDefault();
    [...buscar.closest("[data-asignar-caso]").querySelectorAll("[data-asignar-opcion]")].find((opcion) => !opcion.hidden)?.querySelector("input")?.click();
  });
})();
