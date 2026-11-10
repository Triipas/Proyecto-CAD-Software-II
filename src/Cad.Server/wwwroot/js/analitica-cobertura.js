// «Cobertura por zona» como tablero (16/11/2026): el mapa de fondo, los filtros por bloque y el globo de cada zona. Sin JavaScript
// el tablero se ve igual sobre fondo marino (sin calles), los filtros quedan ocultos y la tabla para lector tiene cada zona.
(() => {
  const seccion = document.querySelector("[data-cobertura]");
  const tablero = seccion?.querySelector("[data-tablero]");
  if (!tablero) return;

  // ---------- El mapa de fondo: quieto y ajustado al encuadre de la cuadrícula ----------
  // El contenedor ya tiene la proporción del encuadre en la proyección del mapa: con zoom fraccionario, el encuadre llena el
  // contenedor y cada zona queda sobre su lugar.
  const fondo = tablero.querySelector("[data-mapa]");
  if (fondo && typeof L !== "undefined") {
    const n = (clave) => parseFloat(tablero.dataset[clave]);
    const encuadre = [[n("sur"), n("oeste")], [n("norte"), n("este")]];
    const mapa = L.map(fondo, {
      zoomControl: false, attributionControl: false, dragging: false, scrollWheelZoom: false, doubleClickZoom: false,
      boxZoom: false, keyboard: false, touchZoom: false, zoomSnap: 0, inertia: false,
    });
    L.tileLayer(tablero.dataset.teselas, { maxZoom: 19 }).addTo(mapa);
    const ajustar = () => { mapa.invalidateSize(false); mapa.fitBounds(encuadre, { padding: [0, 0], animate: false }); };
    ajustar();
    new ResizeObserver(ajustar).observe(tablero);
  }

  // ---------- El globo de cada zona ----------
  const globo = tablero.querySelector("[data-globo]");
  function mostrar(celda) {
    const linea = (texto, clase) => { const s = document.createElement("span"); s.textContent = texto; if (clase) s.className = clase; return s; };
    const titulo = document.createElement("b");
    titulo.textContent = `Zona ${celda.dataset.nombre}`;
    globo.replaceChildren(titulo, ` · bloque ${celda.dataset.bloqueNombre}`, document.createElement("br"),
      linea(celda.dataset.cubierta, celda.classList.contains("celda-b--baja") ? "baja" : ""), document.createElement("br"),
      `${celda.dataset.casos} · ${celda.dataset.horas}`);
    const caja = tablero.getBoundingClientRect(), propia = celda.getBoundingClientRect();
    const abajo = propia.top - caja.top < 70; // en la fila de arriba el globo va debajo, para no salirse del tablero
    globo.classList.toggle("tablero-b__globo--abajo", abajo);
    globo.style.left = `${Math.min(Math.max(propia.left - caja.left + propia.width / 2, 110), caja.width - 110)}px`;
    globo.style.top = `${(abajo ? propia.bottom : propia.top) - caja.top}px`;
    globo.hidden = false;
  }
  const ocultar = () => { globo.hidden = true; };
  for (const celda of tablero.querySelectorAll(".celda-b")) {
    celda.addEventListener("mouseenter", () => mostrar(celda));
    celda.addEventListener("focus", () => mostrar(celda));
    celda.addEventListener("mouseleave", ocultar);
    celda.addEventListener("blur", ocultar);
  }

  // ---------- Los filtros: todos o un bloque (los chips y las tarjetas de bloque hacen lo mismo) ----------
  const chips = seccion.querySelector("[data-chips]");
  const tabla = document.querySelector("[data-cobertura-tabla]");
  const tarjetas = [...seccion.querySelectorAll(".bloque-b")];
  function filtrar(clave) {
    // Tocar el bloque ya elegido vuelve a todos.
    if (clave !== "todos" && seccion.dataset.filtro === clave) clave = "todos";
    seccion.dataset.filtro = clave;
    for (const boton of chips.querySelectorAll("[data-ver]")) boton.setAttribute("aria-pressed", String(boton.dataset.ver === clave));
    for (const tarjeta of tarjetas) tarjeta.setAttribute("aria-pressed", String(tarjeta.dataset.ver === clave));
    for (const parte of [...seccion.querySelectorAll("#respuesta-cobertura [data-bloque]"), ...(tabla?.querySelectorAll("tbody[data-bloque]") ?? [])])
      parte.hidden = parte.dataset.bloque !== clave;
  }
  for (const boton of [...chips.querySelectorAll("[data-ver]"), ...tarjetas]) boton.addEventListener("click", () => filtrar(boton.dataset.ver));
  for (const tarjeta of tarjetas) tarjeta.setAttribute("aria-pressed", "false");
  chips.hidden = false;
})();
