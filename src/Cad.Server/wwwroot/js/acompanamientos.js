// HU38 — vigilancia de acompañamientos. Rediseño (25/10/2026): la central en dos modos sobre un mismo mapa.
//  - «Todas»: cada vigilado en el mapa con su sonar y, en el panel, una cola por urgencia. La lista se vuelve a pedir al
//    servidor (fuente de verdad) cuando llega algo de un ciudadano («AcompanamientoActualizado») o cada 10 s.
//  - «Sesión» (?sesion=…): al elegir a alguien (su tarjeta o su marcador) los demás se desvanecen, se dibuja solo su recorrido
//    y el panel pasa a su ficha. «Todas las sesiones», Escape o «Atrás» del navegador devuelven a «todas».
// Una posición desactualizada (DP12) o una sesión sin conexión (DP20) se dibuja en gris y nunca como actual (CA03).
(() => {
  const raiz = document.querySelector("[data-vigilancia]");
  const region = document.querySelector("[data-region-acompanamientos]");
  const regionSesion = document.querySelector("[data-region-seguimiento]");
  const todas = document.querySelector("[data-modo-todas]");
  const contenedor = document.getElementById("mapa");
  if (!raiz || !region || !contenedor || typeof L === "undefined") return;

  const reducido = matchMedia("(prefers-reduced-motion: reduce)");
  // Sin la marca «Leaflet» (su licencia BSD-2 no la exige en pantalla); el crédito de OpenStreetMap sí se mantiene.
  L.Control.Attribution.prototype.options.prefix = false;
  // Con «reducir movimiento», panel.css anula las transiciones y Leaflet esperaría para siempre el fin de la animación del
  // zoom (las teselas nuevas no llegarían): sin animaciones del mapa en ese caso.
  const sinAnimar = { zoomAnimation: !reducido.matches, markerZoomAnimation: !reducido.matches };
  const mapa = L.map(contenedor, { zoomControl: false, ...sinAnimar }).setView([Number(contenedor.dataset.centroLat), Number(contenedor.dataset.centroLon)], Number(contenedor.dataset.zoom));
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
  const colorRuta = getComputedStyle(document.documentElement).getPropertyValue("--mapa-acompanamiento").trim() || "#0D9488";

  const marcadores = new Map();
  // La sesión que se sigue la dice su ficha (el servidor la pinta con ?sesion=…), no la dirección.
  let sesion = raiz.dataset.modo === "sesion" ? regionSesion?.querySelector("[data-seguimiento]")?.dataset.seguimiento ?? null : null;
  let capaRuta = null;

  const escapar = (texto) => String(texto ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  // El panel deja libre la franja izquierda del mapa: los encuadres la descuentan.
  function margenPanel() {
    const panel = raiz.querySelector(".vigilancia__panel");
    const ancho = panel && getComputedStyle(panel).position === "absolute" ? panel.getBoundingClientRect().width + 32 : 24;
    return { paddingTopLeft: [ancho, 24], paddingBottomRight: [24, 24] };
  }

  // ---------- Marcadores de «todas» ----------
  function dibujar() {
    for (const m of marcadores.values()) m.remove();
    marcadores.clear();
    for (const item of region.querySelectorAll("[data-acompanamiento]")) {
      if (!item.dataset.lat) continue;
      const id = item.dataset.acompanamiento;
      const gris = item.dataset.desactualizada === "true" || item.dataset.sinConexion === "true";
      const pide = item.dataset.atencion === "PideAyuda";
      const texto = `${item.dataset.ciudadano}${gris ? " (última posición conocida)" : ""}: seguir su recorrido`;
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon({
          className: `marcador-acompanamiento${gris ? " marcador-acompanamiento--desactualizado" : " marcador-acompanamiento--vigilado"}${pide ? " marcador-acompanamiento--ayuda" : ""}`,
          html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15], tooltipAnchor: [16, 0],
        }),
        title: texto,
        alt: texto,
        keyboard: true,
      })
        .bindTooltip(escapar(item.dataset.ciudadano), { direction: "right", className: "vigilancia__etiqueta" })
        .on("click", () => elegir(id))
        .addTo(mapa);
      marcadores.set(id, marcador);
    }
    aislar();
  }

  // En «sesión», los demás vigilados se desvanecen; en «todas», vuelven.
  function aislar() {
    for (const [id, marcador] of marcadores) {
      const fuera = sesion !== null && id !== sesion;
      marcador.setOpacity(fuera ? 0 : 1);
      const el = marcador.getElement();
      if (el) {
        el.classList.toggle("marcador--oculto", fuera);
        el.tabIndex = fuera ? -1 : 0;
      }
      if (sesion !== null && id === sesion) marcador.closeTooltip();
    }
    for (const item of region.querySelectorAll("[data-acompanamiento]")) {
      const elegida = item.dataset.acompanamiento === sesion;
      item.classList.toggle("vigilado--elegido", elegida);
      item.querySelector("[data-elegir-acompanamiento]")?.setAttribute("aria-pressed", String(elegida));
    }
  }

  // ---------- Recorrido de la sesión elegida ----------
  function dibujarRuta({ encuadrar = false } = {}) {
    capaRuta?.remove();
    capaRuta = null;
    const ficha = regionSesion?.querySelector("[data-seguimiento]");
    if (!ficha || !ficha.dataset.puntos) return;
    const puntos = ficha.dataset.puntos.split(";").map((p) => p.split(",").map(Number));
    const actual = ficha.dataset.actual === "true";
    capaRuta = L.layerGroup().addTo(mapa);
    const linea = L.polyline([...puntos].reverse(), { color: colorRuta, weight: 5, lineJoin: "round", opacity: 0.9 }).addTo(capaRuta);
    if (puntos.length > 1) {
      L.marker(puntos.at(-1), {
        icon: L.divIcon({ className: "marcador-inicio-recorrido", html: "", iconSize: [14, 14], iconAnchor: [7, 7] }),
        title: "Inicio del recorrido", alt: "Inicio del recorrido", keyboard: false, interactive: false,
      }).addTo(capaRuta);
    }
    // Sin marcador propio para la posición actual: es el de la sesión en el mapa, que queda a la vista.
    if (!marcadores.has(sesion)) {
      L.marker(puntos[0], {
        icon: L.divIcon({ className: `marcador-acompanamiento${actual ? " marcador-acompanamiento--vigilado" : " marcador-acompanamiento--desactualizado"}`, html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15] }),
        title: actual ? "Posición actual" : "Última posición conocida", alt: actual ? "Posición actual" : "Última posición conocida", keyboard: false,
      }).addTo(capaRuta);
    }
    if (!encuadrar) return;
    const opciones = { ...margenPanel(), maxZoom: 17, animate: !reducido.matches };
    if (puntos.length > 1) mapa.flyToBounds(linea.getBounds(), { ...opciones, duration: 0.8 });
    else mapa.flyTo(puntos[0], 17, { animate: !reducido.matches, duration: 0.8 });
  }

  // ---------- Cambio de modo ----------
  async function elegir(id, { historial = true } = {}) {
    const respuesta = await fetch(`/panel/acompanamientos?handler=Sesion&id=${encodeURIComponent(id)}`, { credentials: "same-origin", headers: { Accept: "text/html" } }).catch(() => null);
    if (!respuesta || respuesta.redirected || !respuesta.ok) return; // la sesión ya no existe o terminó la sesión del panel
    regionSesion.innerHTML = await respuesta.text();
    regionSesion.querySelector("[data-seguimiento]")?.classList.add("seguimiento--entra");
    sesion = id;
    raiz.dataset.modo = "sesion";
    todas.hidden = true;
    regionSesion.hidden = false;
    if (historial) history.pushState({ sesion: id }, "", `/panel/acompanamientos?sesion=${encodeURIComponent(id)}`);
    aislar();
    dibujarRuta({ encuadrar: true });
    regionSesion.querySelector("#seguimiento-titulo")?.focus({ preventScroll: true });
    // En pantallas estrechas el mapa va arriba del panel: se lleva a la vista para ver el recorrido aislado.
    if (getComputedStyle(raiz.querySelector(".vigilancia__panel")).position !== "absolute")
      contenedor.scrollIntoView({ block: "start", behavior: reducido.matches ? "auto" : "smooth" });
  }

  function volver({ historial = true } = {}) {
    if (sesion === null) return;
    const anterior = sesion;
    sesion = null;
    raiz.dataset.modo = "todas";
    regionSesion.hidden = true;
    regionSesion.innerHTML = "";
    todas.hidden = false;
    capaRuta?.remove();
    capaRuta = null;
    if (historial) history.pushState({}, "", "/panel/acompanamientos");
    aislar();
    const visibles = [...marcadores.values()].map((m) => m.getLatLng());
    if (visibles.length > 0) mapa.flyToBounds(L.latLngBounds(visibles), { ...margenPanel(), maxZoom: 15, animate: !reducido.matches, duration: 0.6 });
    region.querySelector(`[data-acompanamiento="${CSS.escape(anterior)}"] [data-elegir-acompanamiento]`)?.focus({ preventScroll: true });
  }

  region.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-elegir-acompanamiento]");
    if (boton) elegir(boton.closest("[data-acompanamiento]").dataset.acompanamiento);
  });
  regionSesion.addEventListener("click", (evento) => {
    if (!evento.target.closest("[data-volver-lista]") || evento.ctrlKey || evento.metaKey) return;
    evento.preventDefault();
    volver();
  });
  document.addEventListener("keydown", (evento) => {
    if (evento.key === "Escape" && sesion !== null && !evento.target.closest("input, textarea, select")) volver();
  });
  window.addEventListener("popstate", () => {
    const id = new URLSearchParams(location.search).get("sesion");
    if (id) elegir(id, { historial: false });
    else volver({ historial: false });
  });

  // ---------- Antigüedad al segundo ----------
  // «hace 12 s» / «hace 3 min 5 s», como el servidor.
  function antiguedad(desde) {
    const segundos = Math.max(0, Math.floor((Date.now() - new Date(desde).getTime()) / 1000));
    return segundos < 60 ? `hace ${segundos} s` : `hace ${Math.floor(segundos / 60)} min ${segundos % 60} s`;
  }
  // Tiempo que le queda a la sesión que se sigue (vence a los 30 minutos).
  function restante(hasta) {
    const minutos = Math.ceil((new Date(hasta).getTime() - Date.now()) / 60000);
    return minutos > 0 ? `quedan ${minutos} min` : "vence ahora";
  }
  setInterval(() => {
    for (const span of raiz.querySelectorAll("[data-antiguedad]")) span.textContent = antiguedad(span.dataset.desde);
    for (const span of raiz.querySelectorAll("[data-restante]")) span.textContent = restante(span.dataset.hasta);
  }, 1000);

  // ---------- Actualización desde el servidor ----------
  // El foco del teclado vuelve al mismo botón o enlace de la misma sesión tras cambiar la lista.
  function cambiarConFoco(contenedor, html, clave) {
    const foco = document.activeElement;
    const dentro = contenedor.contains(foco);
    const grupo = dentro ? foco.closest(clave)?.getAttribute(clave.slice(1, -1)) : null;
    const selector = !dentro ? null : foco.matches("[data-elegir-acompanamiento]") ? "[data-elegir-acompanamiento]"
      : foco.id ? `#${CSS.escape(foco.id)}` : foco.matches("a[href]") ? `a[href="${CSS.escape(foco.getAttribute("href"))}"]` : foco.matches("button[type=submit]") ? "button[type=submit]" : null;
    contenedor.innerHTML = html;
    if (!selector) return;
    const destino = grupo ? contenedor.querySelector(`${clave.slice(0, -1)}="${CSS.escape(grupo)}"] ${selector}`) : contenedor.querySelector(selector);
    destino?.focus({ preventScroll: true });
  }

  let pidiendo = false, pendiente = false;
  async function actualizar() {
    if (pidiendo) { pendiente = true; return; }
    pidiendo = true;
    try {
      const respuesta = await fetch("/panel/acompanamientos?handler=Lista", { credentials: "same-origin", headers: { Accept: "text/html" } });
      if (respuesta.redirected || !respuesta.ok) return; // sesión del panel terminada o error: se conserva lo mostrado
      cambiarConFoco(region, await respuesta.text(), "[data-acompanamiento]");
      avisarNuevos();
      dibujar();
      if (sesion !== null) await actualizarSesion();
    } catch {
      // Sin conexión con el servidor: se reintenta en la siguiente vuelta.
    } finally {
      pidiendo = false;
      if (pendiente) { pendiente = false; actualizar(); }
    }
  }

  async function actualizarSesion() {
    const id = sesion;
    const respuesta = await fetch(`/panel/acompanamientos?handler=Sesion&id=${encodeURIComponent(id)}`, { credentials: "same-origin", headers: { Accept: "text/html" } });
    if (respuesta.redirected || !respuesta.ok || sesion !== id) return;
    cambiarConFoco(regionSesion, await respuesta.text(), "[data-seguimiento]");
    dibujarRuta();
  }

  // Un aviso de acompañamiento que aparece en la lista suena una vez (sonidos.js, si están activados).
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
  if (sesion !== null) {
    history.replaceState({ sesion }, "", location.href);
    dibujarRuta({ encuadrar: true });
  } else if (marcadores.size > 0) {
    mapa.fitBounds(L.latLngBounds([...marcadores.values()].map((m) => m.getLatLng())), { ...margenPanel(), maxZoom: 15 });
  }
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
