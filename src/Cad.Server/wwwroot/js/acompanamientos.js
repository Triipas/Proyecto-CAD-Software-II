// HU38 — acompañamientos activos: la lista se vuelve a pedir al servidor cada 10 s (la base de datos es la fuente de verdad)
// y la antigüedad de cada posición avanza cada segundo. Una posición desactualizada (DP12) o una sesión sin conexión (DP20) se
// dibuja en gris y nunca como posición actual (CA03).
// Rediseño CAD v2: «Ver en el mapa» vuela a la sesión, como en la central, y el marcador elegido queda fijado.
(() => {
  const region = document.querySelector("[data-region-acompanamientos]");
  const contenedor = document.getElementById("mapa");
  if (!region || !contenedor || typeof L === "undefined") return;

  const reducido = matchMedia("(prefers-reduced-motion: reduce)");
  const mapa = L.map(contenedor, { zoomControl: false }).setView([Number(contenedor.dataset.centroLat), Number(contenedor.dataset.centroLon)], Number(contenedor.dataset.zoom));
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
  const marcadores = new Map();
  let elegida = null;

  const escapar = (texto) => String(texto ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  function dibujar() {
    for (const m of marcadores.values()) m.remove();
    marcadores.clear();
    for (const item of region.querySelectorAll("[data-acompanamiento]")) {
      if (!item.dataset.lat) continue;
      const id = item.dataset.acompanamiento;
      const gris = item.dataset.desactualizada === "true" || item.dataset.sinConexion === "true";
      const texto = `Acompañamiento de ${item.dataset.ciudadano}${gris ? " (última posición conocida)" : ""}`;
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon({ className: `marcador-acompanamiento${gris ? " marcador-acompanamiento--desactualizado" : ""}`, html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15], popupAnchor: [0, -18] }),
        title: texto,
        alt: texto,
        keyboard: true,
      })
        .bindPopup(`<strong>Acompañamiento</strong><br>${escapar(item.dataset.ciudadano)}<br>${gris ? "Última posición conocida, no actual" : "Posición actual"}`)
        .on("click", () => elegir(id, { desdeMapa: true }))
        .addTo(mapa);
      marcadores.set(id, marcador);
    }
    if (elegida) resaltar(elegida);
  }

  function resaltar(id) {
    for (const [clave, marcador] of marcadores) {
      marcador.getElement()?.classList.toggle("marcador--elegido", clave === id);
      marcador.setZIndexOffset(clave === id ? 1000 : 0);
    }
    for (const item of region.querySelectorAll("[data-acompanamiento]"))
      item.classList.toggle("acompanamiento--elegido", item.dataset.acompanamiento === id);
  }

  // Dos anillos se abren desde el punto al aterrizar (sin ellos si se pide reducir el movimiento).
  function marcarLlegada(punto, gris) {
    if (reducido.matches) return;
    const anillo = L.marker(punto, {
      icon: L.divIcon({ className: `llegada ${gris ? "llegada--gris" : "llegada--acompanamiento"}`, html: "<span></span><span></span>", iconSize: [96, 96], iconAnchor: [48, 48] }),
      interactive: false,
      keyboard: false,
      zIndexOffset: -1000,
    }).addTo(mapa);
    setTimeout(() => anillo.remove(), 1300);
  }

  function elegir(id, { desdeMapa = false } = {}) {
    elegida = id;
    resaltar(id);
    const item = region.querySelector(`[data-acompanamiento="${CSS.escape(id)}"]`);
    if (desdeMapa) {
      item?.scrollIntoView({ block: "nearest", behavior: reducido.matches ? "auto" : "smooth" });
      return;
    }
    const marcador = marcadores.get(id);
    if (!marcador) return;
    const punto = marcador.getLatLng();
    const gris = item?.dataset.desactualizada === "true" || item?.dataset.sinConexion === "true";
    const llegar = () => { marcador.openPopup(); marcarLlegada(punto, gris); };
    const caja = contenedor.getBoundingClientRect();
    if (caja.top < 0 || caja.bottom > innerHeight) contenedor.scrollIntoView({ block: "center", behavior: reducido.matches ? "auto" : "smooth" });
    if (reducido.matches) {
      mapa.setView(punto, 17, { animate: false });
      llegar();
      return;
    }
    mapa.once("moveend", llegar);
    mapa.flyTo(punto, 17, { duration: 0.9, easeLinearity: 0.2 });
  }

  region.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-ver-en-mapa]");
    if (boton) elegir(boton.closest("[data-acompanamiento]").dataset.acompanamiento);
  });

  // «hace 12 s» / «hace 3 min 5 s», como el servidor.
  function antiguedad(desde) {
    const segundos = Math.max(0, Math.floor((Date.now() - new Date(desde).getTime()) / 1000));
    return segundos < 60 ? `hace ${segundos} s` : `hace ${Math.floor(segundos / 60)} min ${segundos % 60} s`;
  }

  setInterval(() => {
    for (const span of region.querySelectorAll("[data-antiguedad]")) span.textContent = antiguedad(span.dataset.desde);
  }, 1000);

  async function actualizar() {
    try {
      const respuesta = await fetch("/panel/acompanamientos?handler=Lista", { credentials: "same-origin", headers: { Accept: "text/html" } });
      if (respuesta.redirected || !respuesta.ok) return; // sesión terminada o error: se conserva lo último mostrado
      region.innerHTML = await respuesta.text();
      dibujar();
    } catch {
      // Sin conexión con el servidor: se reintenta en la siguiente vuelta.
    }
  }

  dibujar();
  setInterval(actualizar, 10000);
})();
