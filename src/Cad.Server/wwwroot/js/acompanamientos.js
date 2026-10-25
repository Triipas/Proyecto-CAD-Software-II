// HU38 — acompañamientos activos: la lista se vuelve a pedir al servidor cada 10 s (la base de datos es la fuente de verdad)
// y la antigüedad de cada posición avanza cada segundo. Una posición desactualizada (DP12) o una sesión sin conexión (DP20) se
// dibuja en gris y nunca como posición actual (CA03).
// Rediseño CAD v2: elegir una sesión vuela a ella en el mapa, como en la central, y el marcador elegido queda fijado.
// 24/10/2026: la tarjeta entera es el botón que la elige, y la lista se actualiza en cuanto llega algo del ciudadano (aviso
// «AcompanamientoActualizado» del canal de la central); la consulta cada 10 s queda de respaldo y para el umbral de DP20.
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
        icon: L.divIcon({ className: `marcador-acompanamiento${gris ? " marcador-acompanamiento--desactualizado" : " marcador-acompanamiento--vigilado"}`, html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15], popupAnchor: [0, -18] }),
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
    for (const item of region.querySelectorAll("[data-acompanamiento]")) {
      item.classList.toggle("acompanamiento--elegido", item.dataset.acompanamiento === id);
      item.querySelector("[data-elegir-acompanamiento]")?.setAttribute("aria-pressed", String(item.dataset.acompanamiento === id));
    }
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
    const boton = evento.target.closest("[data-elegir-acompanamiento]");
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

  let pidiendo = false, pendiente = false;
  async function actualizar() {
    if (pidiendo) { pendiente = true; return; }
    pidiendo = true;
    try {
      const respuesta = await fetch("/panel/acompanamientos?handler=Lista", { credentials: "same-origin", headers: { Accept: "text/html" } });
      if (respuesta.redirected || !respuesta.ok) return; // sesión terminada o error: se conserva lo último mostrado
      // El foco del teclado vuelve al mismo botón o enlace de la misma sesión tras cambiar la lista.
      const foco = document.activeElement;
      const sesionConFoco = region.contains(foco) ? foco.closest("[data-acompanamiento]")?.dataset.acompanamiento : null;
      const selectorFoco = foco?.matches?.("[data-elegir-acompanamiento]") ? "[data-elegir-acompanamiento]" : foco?.matches?.("a") ? `a[href="${foco.getAttribute("href")}"]` : null;
      region.innerHTML = await respuesta.text();
      if (sesionConFoco && selectorFoco)
        region.querySelector(`[data-acompanamiento="${CSS.escape(sesionConFoco)}"] ${selectorFoco}`)?.focus({ preventScroll: true });
      avisarNuevos();
      dibujar();
    } catch {
      // Sin conexión con el servidor: se reintenta en la siguiente vuelta.
    } finally {
      pidiendo = false;
      if (pendiente) { pendiente = false; actualizar(); }
    }
  }

  // 24/10/2026: un aviso de acompañamiento que aparece en la lista suena una vez (sonidos.js, si están activados).
  const avisosVistos = new Set([...region.querySelectorAll("[data-aviso]")].map((el) => el.dataset.aviso));
  function avisarNuevos() {
    let hayNuevo = false;
    for (const el of region.querySelectorAll("[data-aviso]")) {
      if (avisosVistos.has(el.dataset.aviso)) continue;
      avisosVistos.add(el.dataset.aviso);
      hayNuevo = true;
    }
    if (hayNuevo) window.CadSonidos?.reproducir("aviso-acompanamiento");
  }

  dibujar();
  setInterval(actualizar, 10000);
  if (typeof signalR !== "undefined") {
    const conexion = new signalR.HubConnectionBuilder().withUrl("/hubs/central").withAutomaticReconnect().build();
    conexion.on("AcompanamientoActualizado", () => actualizar());
    // El SOS de «Necesito ayuda» puede cancelarse o cerrarse: la tarjeta deja de mostrarlo como alerta.
    conexion.on("SosCancelado", () => actualizar());
    conexion.on("CasoActualizado", () => actualizar());
    conexion.onreconnected(() => actualizar());
    conexion.start().catch(() => { /* sin canal en tiempo real: queda la consulta cada 10 s */ });
  }
})();
