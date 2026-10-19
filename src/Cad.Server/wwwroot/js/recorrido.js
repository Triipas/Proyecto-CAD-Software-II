// HU38 y HU39-A — recorrido de una sesión de acompañamiento: línea de las posiciones y la más reciente marcada. Solo se
// marca como actual si la sesión está activa, con conexión y la posición no está desactualizada.
// Rediseño CAD v2: el inicio lleva su propia marca y, al abrir, dos anillos señalan la última posición.
(() => {
  const contenedor = document.getElementById("mapa-recorrido");
  if (!contenedor || typeof L === "undefined") return;
  const puntos = contenedor.dataset.puntos.split(";").map((p) => p.split(",").map(Number));
  const mapa = L.map(contenedor, { zoomControl: false });
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
  const color = getComputedStyle(document.documentElement).getPropertyValue("--mapa-acompanamiento").trim() || "#0D9488";
  const linea = L.polyline([...puntos].reverse(), { color, weight: 4, lineJoin: "round" }).addTo(mapa);
  const actual = contenedor.dataset.actual === "true";
  const texto = actual ? "Posición actual" : "Última posición conocida";
  if (puntos.length > 1) {
    L.marker(puntos.at(-1), {
      icon: L.divIcon({ className: "marcador-inicio-recorrido", html: "", iconSize: [14, 14], iconAnchor: [7, 7] }),
      title: "Inicio del recorrido",
      alt: "Inicio del recorrido",
      keyboard: false,
    }).addTo(mapa);
  }
  L.marker(puntos[0], {
    icon: L.divIcon({ className: `marcador-acompanamiento${actual ? "" : " marcador-acompanamiento--desactualizado"}`, html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15], popupAnchor: [0, -18] }),
    title: texto,
    alt: texto,
    keyboard: true,
  }).bindPopup(texto).addTo(mapa);
  mapa.fitBounds(linea.getBounds().pad(0.3), { maxZoom: 17 });

  if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  setTimeout(() => {
    const anillo = L.marker(puntos[0], {
      icon: L.divIcon({ className: `llegada ${actual ? "llegada--acompanamiento" : "llegada--gris"}`, html: "<span></span><span></span>", iconSize: [96, 96], iconAnchor: [48, 48] }),
      interactive: false,
      keyboard: false,
      zIndexOffset: -1000,
    }).addTo(mapa);
    setTimeout(() => anillo.remove(), 1300);
  }, 250);
})();
