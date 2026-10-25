// HU10/HU14 — mapa de solo lectura con el lugar del hecho de un reporte (Leaflet + OpenStreetMap).
// También lo usa el detalle de SOS (data-clase-marcador y data-titulo) para la ubicación del dispositivo.
(() => {
  const contenedor = document.getElementById("mapa-hecho");
  if (!contenedor || typeof L === "undefined") return;
  const punto = [Number(contenedor.dataset.lat), Number(contenedor.dataset.lon)];
  // Sin la marca «Leaflet» (su licencia BSD-2 no la exige en pantalla); el crédito de OpenStreetMap sí se mantiene.
  L.Control.Attribution.prototype.options.prefix = false;
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

  // Al abrir el detalle, dos anillos se abren una vez desde el punto (sin ellos si se pide reducir el movimiento).
  if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const reporte = (contenedor.dataset.claseMarcador ?? "").includes("reporte");
  const anillo = L.marker(punto, {
    icon: L.divIcon({ className: `llegada${reporte ? " llegada--reporte" : ""}`, html: "<span></span><span></span>", iconSize: [96, 96], iconAnchor: [48, 48] }),
    interactive: false,
    keyboard: false,
    zIndexOffset: -1000,
  });
  setTimeout(() => { anillo.addTo(mapa); setTimeout(() => anillo.remove(), 1300); }, 250);
})();
