// «Salud del sistema» (16/11/2026): el margen del GPS de los SOS, a escala sobre el mapa. Dos círculos con el radio real (la mitad
// de los SOS tiene este margen o menos; el 10 % peor, más que el grande) en el centro del distrito. El mapa no se mueve: es una
// regla: se puede acercar con la rueda o los botones y moverlo, pero arranca encuadrado. Sin JavaScript queda la frase con las dos cifras.
(() => {
  const caja = document.querySelector("[data-gps]");
  if (!caja || typeof L === "undefined") return;
  const p50 = parseFloat(caja.dataset.p50), p90 = parseFloat(caja.dataset.p90);
  if (!(p50 > 0)) return;
  const centro = [-16.4309, -71.5225]; // el centro de los tres bloques del distrito
  const estilo = getComputedStyle(document.documentElement);
  const advertencia = estilo.getPropertyValue("--color-advertencia").trim() || "#9A6700";

  const mapa = L.map(caja, {
    zoomControl: false, attributionControl: true, scrollWheelZoom: true, wheelPxPerZoomLevel: 90, zoomSnap: 0.25,
  }).setView(centro, 15); // una vista inicial: los círculos necesitan el mapa armado para medir su encuadre
  L.Control.Attribution.prototype.options.prefix = false;
  L.tileLayer(caja.dataset.teselas, { maxZoom: 19, attribution: "© colaboradores de OpenStreetMap" }).addTo(mapa);
  const grande = L.circle(centro, { radius: Math.max(p90, p50), color: advertencia, weight: 2, dashArray: "6 5", fillColor: advertencia, fillOpacity: 0.08, interactive: false }).addTo(mapa);
  L.circle(centro, { radius: p50, color: "#037F58", weight: 2, fillColor: "#45E3A4", fillOpacity: 0.35, interactive: false }).addTo(mapa);
  L.circleMarker(centro, { radius: 4, color: "#FFFFFF", weight: 2, fillColor: "#0A1A3A", fillOpacity: 1, interactive: false }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
  let ajustado = false;
  const ajustar = () => { mapa.invalidateSize(false); if (!ajustado) { mapa.fitBounds(grande.getBounds(), { padding: [24, 24], animate: false }); ajustado = true; } };
  ajustar();
  new ResizeObserver(ajustar).observe(caja);
})();
