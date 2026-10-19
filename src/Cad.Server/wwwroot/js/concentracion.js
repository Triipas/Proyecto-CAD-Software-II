// HU29 — mapa de concentración: una marca por celda de la cuadrícula del servidor, más grande e intensa cuantos más casos
// agrupa, con el número dentro. SOS en cuadros rojos y reportes en círculos violetas (CA04: se diferencian por color, forma
// y texto). Si una zona tiene de los dos, se dibujan uno junto al otro.
(() => {
  const contenedor = document.getElementById("mapa-concentracion");
  if (!contenedor || typeof L === "undefined") return;
  const celdas = JSON.parse(document.getElementById("celdas-concentracion").textContent);
  const maximo = Math.max(1, Number(contenedor.dataset.maximo));
  const mapa = L.map(contenedor, { zoomControl: false }).setView([Number(contenedor.dataset.centroLat), Number(contenedor.dataset.centroLon)], Number(contenedor.dataset.zoom));
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);

  const zonas = new Set();
  const marcas = [];
  for (const celda of celdas) {
    const proporcion = celda.casos / maximo;
    const lado = Math.round(22 + 26 * Math.sqrt(proporcion));
    const centro = [(celda.sur + celda.norte) / 2, (celda.oeste + celda.este) / 2];
    const zona = centro.join(",");
    const compartida = zonas.has(zona);
    zonas.add(zona);
    const sos = celda.origen === "SOS";
    const texto = `${sos ? "SOS" : "Reportes"}: ${celda.casos} ${celda.casos === 1 ? "caso" : "casos"} en esta zona`;
    const marca = L.marker(centro, {
      icon: L.divIcon({
        className: `celda-concentracion celda-concentracion--${sos ? "sos" : "reporte"}`,
        // --i escalona la aparición de las marcas (rediseño CAD v2).
        html: `<span style="--intensidad:${(0.35 + 0.6 * proporcion).toFixed(2)};--i:${Math.min(marcas.length, 12)}">${celda.casos}</span>`,
        iconSize: [lado, lado],
        // La segunda marca de una misma zona se desplaza para que las dos se vean.
        iconAnchor: [compartida ? -2 : lado / 2, lado / 2],
      }),
      title: texto,
      alt: texto,
      keyboard: true,
    }).bindPopup(texto).addTo(mapa);
    marcas.push(marca);
  }
  if (marcas.length) mapa.fitBounds(L.latLngBounds(marcas.map((m) => m.getLatLng())).pad(0.15), { maxZoom: 15 });
})();
