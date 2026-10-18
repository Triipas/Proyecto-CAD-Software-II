// HU10/HU14 — mapa de solo lectura con el lugar del hecho de un reporte (Leaflet + OpenStreetMap).
// También lo usa el detalle de SOS (data-clase-marcador y data-titulo) para la ubicación del dispositivo.
(() => {
  const contenedor = document.getElementById("mapa-hecho");
  if (!contenedor || typeof L === "undefined") return;
  const punto = [Number(contenedor.dataset.lat), Number(contenedor.dataset.lon)];
  const mapa = L.map(contenedor).setView(punto, 16);
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.marker(punto, {
    icon: L.divIcon({ className: contenedor.dataset.claseMarcador ?? "marcador-hecho", html: "<span></span>", iconSize: [22, 22], iconAnchor: [11, 26] }),
    title: contenedor.dataset.titulo ?? "Lugar del hecho",
    alt: contenedor.dataset.titulo ?? "Lugar del hecho",
    keyboard: false,
  }).addTo(mapa);
})();
