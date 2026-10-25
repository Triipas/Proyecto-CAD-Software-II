// HU38 y HU39-A — recorrido de una sesión de acompañamiento: línea de las posiciones y la más reciente marcada. Solo se
// marca como actual si la sesión está activa, con conexión y la posición no está desactualizada.
// Rediseño CAD v2: el inicio lleva su propia marca y, al abrir, dos anillos señalan la última posición.
// 24/10/2026: con la sesión activa, acompanamiento-detalle.js llama a CadRecorrido.actualizar() cuando llegan posiciones
// nuevas: el mapa no se vuelve a crear (no pierde el acercamiento del operador) y solo se mueve si la nueva queda fuera.
(() => {
  const contenedor = document.getElementById("mapa-recorrido");
  if (!contenedor || typeof L === "undefined") return;
  const reducido = matchMedia("(prefers-reduced-motion: reduce)");
  const color = getComputedStyle(document.documentElement).getPropertyValue("--mapa-acompanamiento").trim() || "#0D9488";
  let mapa = null, linea = null, inicio = null, ultima = null, clave = "";

  const leer = (texto) => (texto ? texto.split(";").map((p) => p.split(",").map(Number)) : []);

  function anillo(punto, actual) {
    if (reducido.matches) return;
    const marca = L.marker(punto, {
      icon: L.divIcon({ className: `llegada ${actual ? "llegada--acompanamiento" : "llegada--gris"}`, html: "<span></span><span></span>", iconSize: [96, 96], iconAnchor: [48, 48] }),
      interactive: false,
      keyboard: false,
      zIndexOffset: -1000,
    }).addTo(mapa);
    setTimeout(() => marca.remove(), 1300);
  }

  function dibujar(textoPuntos, actual) {
    const puntos = leer(textoPuntos);
    if (puntos.length === 0) return;
    const nueva = `${textoPuntos}|${actual}`;
    if (nueva === clave) return; // nada cambió
    const primeraVez = mapa === null;
    clave = nueva;
    contenedor.hidden = false;
    if (primeraVez) {
      // Sin la marca «Leaflet» (su licencia BSD-2 no la exige en pantalla); el crédito de OpenStreetMap sí se mantiene.
      L.Control.Attribution.prototype.options.prefix = false;
      mapa = L.map(contenedor, { zoomControl: false });
      L.tileLayer(contenedor.dataset.teselas, {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
      }).addTo(mapa);
      L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
    }
    const anterior = ultima?.getLatLng();
    linea?.remove();
    inicio?.remove();
    ultima?.remove();
    linea = L.polyline([...puntos].reverse(), { color, weight: 4, lineJoin: "round" }).addTo(mapa);
    const texto = actual ? "Posición actual" : "Última posición conocida";
    if (puntos.length > 1) {
      inicio = L.marker(puntos.at(-1), {
        icon: L.divIcon({ className: "marcador-inicio-recorrido", html: "", iconSize: [14, 14], iconAnchor: [7, 7] }),
        title: "Inicio del recorrido",
        alt: "Inicio del recorrido",
        keyboard: false,
      }).addTo(mapa);
    }
    ultima = L.marker(puntos[0], {
      icon: L.divIcon({ className: `marcador-acompanamiento${actual ? " marcador-acompanamiento--vigilado" : " marcador-acompanamiento--desactualizado"}`, html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15], popupAnchor: [0, -18] }),
      title: texto,
      alt: texto,
      keyboard: true,
    }).bindPopup(texto).addTo(mapa);

    if (primeraVez) {
      mapa.fitBounds(linea.getBounds().pad(0.3), { maxZoom: 17 });
      setTimeout(() => anillo(puntos[0], actual), 250);
      return;
    }
    const punto = L.latLng(puntos[0]);
    if (!mapa.getBounds().contains(punto)) mapa.panTo(punto, { animate: !reducido.matches });
    if (!anterior || !anterior.equals(punto)) anillo(puntos[0], actual);
  }

  dibujar(contenedor.dataset.puntos, contenedor.dataset.actual === "true");
  window.CadRecorrido = { actualizar: dibujar };
})();
