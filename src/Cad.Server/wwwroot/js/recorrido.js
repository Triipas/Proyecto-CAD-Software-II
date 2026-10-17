// HU38 y HU39-A — recorrido de una sesión de acompañamiento: línea de las posiciones y la más reciente marcada. Solo se
// marca como actual si la sesión está activa, con conexión y la posición no está desactualizada.
(() => {
  const contenedor = document.getElementById("mapa-recorrido");
  if (!contenedor || typeof L === "undefined") return;
  const puntos = contenedor.dataset.puntos.split(";").map((p) => p.split(",").map(Number));
  const mapa = L.map(contenedor);
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  const linea = L.polyline([...puntos].reverse(), { color: "#047857", weight: 4 }).addTo(mapa);
  const actual = contenedor.dataset.actual === "true";
  const texto = actual ? "Posición actual" : "Última posición conocida";
  L.marker(puntos[0], {
    icon: L.divIcon({ className: `marcador-acompanamiento${actual ? "" : " marcador-acompanamiento--desactualizado"}`, html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15] }),
    title: texto,
    alt: texto,
    keyboard: true,
  }).bindPopup(texto).addTo(mapa);
  mapa.fitBounds(linea.getBounds().pad(0.3), { maxZoom: 17 });
})();
