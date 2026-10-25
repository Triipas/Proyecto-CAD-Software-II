// Reporte registrado por la central (23/10/2026): un toque en el mapa marca el lugar del hecho (Leaflet + OpenStreetMap).
// Las coordenadas van en campos ocultos; sin JavaScript se puede registrar igual con una referencia.
(() => {
  const contenedor = document.getElementById("mapa-nuevo-reporte");
  if (!contenedor || typeof L === "undefined") return;
  const latitud = document.querySelector("[data-latitud]");
  const longitud = document.querySelector("[data-longitud]");
  const texto = document.querySelector("[data-punto]");
  const quitar = document.querySelector("[data-quitar-punto]");

  // Sin la marca «Leaflet» (su licencia BSD-2 no la exige en pantalla); el crédito de OpenStreetMap sí se mantiene.
  L.Control.Attribution.prototype.options.prefix = false;
  const mapa = L.map(contenedor).setView([Number(contenedor.dataset.lat), Number(contenedor.dataset.lon)], 14);
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  let marcador = null;

  function marcar(punto, centrar) {
    if (marcador) marcador.setLatLng(punto);
    else marcador = L.marker(punto, {
      icon: L.divIcon({ className: "marcador-hecho marcador-hecho--reporte", html: "<span></span>", iconSize: [22, 22], iconAnchor: [11, 26] }),
      title: "Lugar del hecho",
      alt: "Lugar del hecho",
      keyboard: false,
    }).addTo(mapa);
    latitud.value = punto.lat.toFixed(6);
    longitud.value = punto.lng.toFixed(6);
    texto.textContent = `Punto marcado: ${latitud.value}, ${longitud.value}.`;
    quitar.hidden = false;
    if (centrar) mapa.setView(punto, 16);
  }

  mapa.on("click", (e) => marcar(e.latlng, false));
  quitar.addEventListener("click", () => {
    marcador?.remove();
    marcador = null;
    latitud.value = longitud.value = "";
    texto.textContent = "Sin punto en el mapa: se usará la referencia.";
    quitar.hidden = true;
  });

  // Tras un envío con errores, el punto elegido se conserva.
  if (latitud.value && longitud.value) marcar(L.latLng(Number(latitud.value), Number(longitud.value)), true);
})();
