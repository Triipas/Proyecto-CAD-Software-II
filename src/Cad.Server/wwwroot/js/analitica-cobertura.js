// «Cobertura por zona» como tablero (16/11/2026): el mapa (que se mueve y se acerca con la rueda), los filtros por bloque y el globo de cada zona. Sin JavaScript
// el tablero se ve igual sobre fondo marino (sin calles), los filtros quedan ocultos y la tabla para lector tiene cada zona.
(() => {
  const seccion = document.querySelector("[data-cobertura]");
  const tablero = seccion?.querySelector("[data-tablero]");
  if (!tablero) return;

  // ---------- El mapa: se arrastra y se acerca, y las zonas van con él ----------
  // Arranca ajustado al encuadre de la cuadrícula (el contenedor ya tiene su proporción en la proyección del mapa). La capa de zonas y
  // serenos se muda a un panel propio del mapa: Leaflet la arrastra junto con las calles, y en cada zoom se recoloca sobre las
  // esquinas reales del encuadre, así cada zona sigue sobre su lugar.
  const fondo = tablero.querySelector("[data-mapa]");
  const capa = tablero.querySelector("[data-capa]");
  if (fondo && capa && typeof L !== "undefined") {
    const n = (clave) => parseFloat(tablero.dataset[clave]);
    const noroeste = [n("norte"), n("oeste")], sureste = [n("sur"), n("este")];
    const mapa = L.map(fondo, { zoomControl: false, attributionControl: false, zoomSnap: 0, zoomDelta: 0.5, wheelPxPerZoomLevel: 90, zoomAnimation: false });
    L.tileLayer(tablero.dataset.teselas, { maxZoom: 19 }).addTo(mapa);
    L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
    const panel = mapa.createPane("tablero");
    panel.style.zIndex = 450;
    panel.appendChild(capa);
    // El fondo era decorativo (aria-hidden); ahora tiene las zonas y los botones de zoom: solo las calles quedan fuera del lector.
    fondo.removeAttribute("aria-hidden");
    mapa.getPane("tilePane").setAttribute("aria-hidden", "true");
    tablero.classList.add("tablero-b--mapa");
    function colocar() {
      const a = mapa.latLngToLayerPoint(noroeste), b = mapa.latLngToLayerPoint(sureste);
      L.DomUtil.setPosition(capa, a);
      capa.style.width = `${b.x - a.x}px`;
      capa.style.height = `${b.y - a.y}px`;
    }
    mapa.on("zoomend viewreset", colocar);
    let ajustado = false;
    const ajustar = () => {
      mapa.invalidateSize(false);
      if (!ajustado) { mapa.fitBounds([noroeste, sureste], { padding: [0, 0], animate: false }); ajustado = true; }
      colocar();
    };
    ajustar();
    new ResizeObserver(ajustar).observe(tablero);
    // El globo se calcula sobre el tablero: al mover el mapa se esconde para no quedar flotando lejos de su zona.
    mapa.on("movestart zoomstart", () => { const g = tablero.querySelector("[data-globo]"); if (g) g.hidden = true; });
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
