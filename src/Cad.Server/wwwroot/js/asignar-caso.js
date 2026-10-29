// Asignación en el detalle de un SOS o de un reporte (29/10/2026): filtro por nombre de la lista «Elegir a otro sereno».
// Sin JavaScript la lista se ve completa y se elige igual; el filtro solo ayuda cuando crece.
(() => {
  const sinTildes = (texto) => texto.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();

  for (const bloque of document.querySelectorAll("[data-asignar-caso]")) {
    const buscar = bloque.querySelector("[data-asignar-buscar]");
    if (!buscar) continue;
    const opciones = [...bloque.querySelectorAll("[data-asignar-opcion]")];
    const sinResultados = bloque.querySelector("[data-asignar-sin-resultados]");

    buscar.addEventListener("input", () => {
      const consulta = sinTildes(buscar.value.trim());
      let visibles = 0;
      for (const opcion of opciones) {
        const coincide = sinTildes(opcion.dataset.nombre).includes(consulta);
        opcion.hidden = !coincide;
        if (coincide) visibles++;
      }
      if (sinResultados) sinResultados.hidden = visibles > 0;
    });

    // Intro en el buscador no envía el formulario sin sereno elegido: elige al primero visible.
    buscar.addEventListener("keydown", (evento) => {
      if (evento.key !== "Enter") return;
      evento.preventDefault();
      opciones.find((opcion) => !opcion.hidden)?.querySelector("input")?.click();
    });
  }
})();
