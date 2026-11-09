// HU10/HU14 — mapa de solo lectura con el lugar del hecho de un reporte (Leaflet + OpenStreetMap).
// También lo usa el detalle de SOS (data-clase-marcador y data-titulo) para la ubicación del dispositivo.
// 05/11/2026: con los serenos (_SerenosMinimapa): sus marcadores, el recorrido por calles del que tiene asignado el caso y una
// línea recta, con su distancia, hasta el sereno elegido para asignar o reasignar (el sugerido, hasta que se elige otro).
(() => {
  const contenedor = document.getElementById("mapa-hecho");
  if (!contenedor || typeof L === "undefined") return;
  const reducido = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const punto = L.latLng(Number(contenedor.dataset.lat), Number(contenedor.dataset.lon));
  // Sin la marca «Leaflet» (su licencia BSD-2 no la exige en pantalla); el crédito de OpenStreetMap sí se mantiene.
  L.Control.Attribution.prototype.options.prefix = false;
  const mapa = L.map(contenedor).setView(punto, 16);
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  // El mismo marcador que el caso tiene en la central (marcadores.js).
  const reporte = (contenedor.dataset.claseMarcador ?? "").includes("reporte");
  L.marker(punto, {
    icon: L.divIcon(cadMarcadores.caso(reporte, "marcador-hecho")),
    title: contenedor.dataset.titulo ?? "Lugar del hecho",
    alt: contenedor.dataset.titulo ?? "Lugar del hecho",
    keyboard: false,
    zIndexOffset: 500,
  }).addTo(mapa);

  // ---------- Serenos ----------
  const lista = document.querySelector("[data-serenos-minimapa]");
  const serenos = new Map(); // id → { datos, marcador, punto }
  for (const s of lista?.querySelectorAll("[data-sereno]") ?? []) {
    const asignado = s.dataset.asignado === "true";
    const disponible = s.dataset.disponible === "true";
    const estado = asignado ? "asignado a este caso" : disponible ? "disponible" : "no disponible";
    const texto = `${s.dataset.nombre} · ${estado} · a ${s.dataset.distancia}${s.dataset.hora ? ` · ubicación de las ${s.dataset.hora}` : ""}`;
    const lugar = L.latLng(Number(s.dataset.lat), Number(s.dataset.lon));
    const marcador = L.marker(lugar, {
      icon: L.divIcon(cadMarcadores.sereno(disponible)),
      title: texto,
      alt: texto,
      keyboard: false,
    }).addTo(mapa);
    serenos.set(s.dataset.sereno, { datos: s.dataset, marcador, punto: lugar });
  }

  // El sereno con el caso asignado: su recorrido por calles (o una línea punteada si no hay ruta) hasta el caso.
  const asignado = [...serenos.values()].find((s) => s.datos.asignado === "true");
  let recorrido = []; // lo que el encuadre debe dejar a la vista del recorrido del asignado
  if (asignado) {
    let puntos = null;
    try { puntos = JSON.parse(lista.dataset.ruta || "null"); } catch { puntos = null; }
    const trazo = { lineCap: "round", lineJoin: "round", interactive: false, className: "linea-vinculo" };
    if (Array.isArray(puntos) && puntos.length > 1) {
      L.polyline(puntos, { ...trazo, color: "#FFFFFF", weight: 8, opacity: 0.9 }).addTo(mapa);
      L.polyline(puntos, { ...trazo, color: "#0A1A3A", weight: 4, opacity: 0.9 }).addTo(mapa);
      recorrido = puntos.map((p) => L.latLng(p));
      // En un minimapa estático, el primer tramo de la ruta permite orientar al asignado sin inventar un rumbo al destino.
      const siguiente = recorrido.find((p) => asignado.punto.distanceTo(p) >= 4);
      if (siguiente) {
        const inicial = cadMarcadores.orientarSereno(null, asignado.punto);
        cadMarcadores.actualizarSereno(asignado.marcador, asignado.datos.disponible === "true", cadMarcadores.orientarSereno(inicial, siguiente));
      }
    } else {
      L.polyline([asignado.punto, punto], { ...trazo, color: "#0A1A3A", weight: 4, opacity: 0.85, dashArray: "1 9" }).addTo(mapa);
    }
    recorrido.push(asignado.punto);
  }

  // ---------- Sereno elegido para asignar: línea recta con su distancia ----------
  const color = getComputedStyle(document.documentElement).getPropertyValue("--color-primario").trim() || "#00805A";
  let linea = null;
  let elegido = null;

  // El caso, el recorrido del asignado (si hay) y el sereno elegido, a la vista a la vez.
  function encuadrar(otro, animar) {
    const puntos = [punto, ...recorrido, ...(otro ? [otro] : [])];
    if (puntos.length < 2) return;
    mapa.fitBounds(L.latLngBounds(puntos), { padding: [36, 36], maxZoom: 17, animate: animar && !reducido });
  }

  function elegir(id, { animar = true } = {}) {
    if (elegido) {
      elegido.marcador.getElement()?.classList.remove("marcador--elegido");
      elegido.marcador.setZIndexOffset(0);
    }
    linea?.remove();
    linea = null;
    elegido = serenos.get(id) ?? null;
    if (!elegido) return;
    elegido.marcador.getElement()?.classList.add("marcador--elegido");
    elegido.marcador.setZIndexOffset(1000);
    linea = L.polyline([punto, elegido.punto], { color, weight: 3, dashArray: "8 7", interactive: false, className: "linea-despacho" })
      .bindTooltip(elegido.datos.distancia, { permanent: true, direction: "center", className: "etiqueta-despacho" })
      .addTo(mapa);
    encuadrar(elegido.punto, animar);
  }

  // Asignar: el sugerido viene elegido; al marcar a otro de la lista, la línea pasa a él. Reasignar: el que se marque.
  const RADIOS = 'input[type="radio"][name="Asignacion.SerenoId"], input[type="radio"][name="Reasignacion.SerenoId"]';
  const sugerido = document.querySelector('.asignar__sugerido input[name="Asignacion.SerenoId"]');
  const marcado = [...document.querySelectorAll(RADIOS)].find((r) => r.checked);
  document.addEventListener("change", (evento) => {
    if (evento.target.matches?.(RADIOS) && evento.target.checked) elegir(evento.target.value);
  });
  if (marcado || sugerido) elegir((marcado ?? sugerido).value, { animar: false });
  else if (asignado) encuadrar(null, false);

  // Al abrir el detalle, dos anillos se abren una vez desde el punto (sin ellos si se pide reducir el movimiento).
  if (reducido) return;
  const anillo = L.marker(punto, {
    icon: L.divIcon({ className: `llegada${reporte ? " llegada--reporte" : ""}`, html: "<span></span><span></span>", iconSize: [96, 96], iconAnchor: [48, 48] }),
    interactive: false,
    keyboard: false,
    zIndexOffset: -1000,
  });
  setTimeout(() => { anillo.addTo(mapa); setTimeout(() => anillo.remove(), 1300); }, 250);
})();
