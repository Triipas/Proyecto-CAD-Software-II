// HU38 — vigilancia de acompañamientos. Rediseño (25/10/2026): la central en modos sobre un mismo mapa.
//  - «Todas»: cada vigilado en el mapa con su sonar y, en el panel, una cola por urgencia. La lista se vuelve a pedir al
//    servidor (fuente de verdad) cuando llega algo de un ciudadano («AcompanamientoActualizado») o cada 10 s.
//  - «Sesión» (?sesion=…): al elegir a alguien (su tarjeta o su marcador) los demás se desvanecen, se dibuja solo su recorrido
//    y el panel pasa a su ficha. «Todas las sesiones», Escape o «Atrás» del navegador devuelven a «todas».
//  - «Detalle» (/panel/acompanamientos/{id}, 26/10/2026): la ficha completa en el panel ancho, sin recargar la página ni el
//    mapa. Cada posición de su línea de tiempo lleva el mapa a ese punto. «Volver a la vigilancia» o Escape vuelven a «sesión».
// Una posición desactualizada (DP12) o una sesión sin conexión (DP20) se dibuja en gris y nunca como actual (CA03).
// Un solo marcador en los tres modos: el círculo con un punto blanco al centro y, con la posición al día, su sonar.
(() => {
  const raiz = document.querySelector("[data-vigilancia]");
  const region = document.querySelector("[data-region-acompanamientos]");
  const regionSesion = document.querySelector("[data-region-seguimiento]");
  const todas = document.querySelector("[data-modo-todas]");
  const panel = raiz?.querySelector(".vigilancia__panel");
  const contenedor = document.getElementById("mapa");
  if (!raiz || !region || !regionSesion || !panel || !contenedor || typeof L === "undefined") return;
  // El detalle se cambia entero al abrir otra sesión: se busca siempre de nuevo.
  let regionDetalle = raiz.querySelector("[data-region-detalle]");

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
  let modo = raiz.dataset.modo;
  // La sesión que se sigue la dice su ficha (el servidor la pinta), no la dirección.
  let sesion = modo === "sesion" ? regionSesion.querySelector("[data-seguimiento]")?.dataset.seguimiento ?? null
    : modo === "detalle" ? regionDetalle?.dataset.sesion ?? null : null;
  let detalle = null; // controlador de acompanamiento-detalle.js mientras el modo es «detalle»

  const escapar = (texto) => String(texto ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  // El marcador de un vigilado; el punto blanco va en el <span> (los anillos del sonar usan ::before y ::after).
  const icono = (clases) => L.divIcon({ className: `marcador-acompanamiento ${clases}`, html: "<span></span>", iconSize: [30, 30], iconAnchor: [15, 15], tooltipAnchor: [16, 0] });

  // El panel deja libre la franja izquierda del mapa: los encuadres la descuentan.
  function margenPanel() {
    const ancho = getComputedStyle(panel).position === "absolute" ? panel.getBoundingClientRect().width + 32 : 24;
    return { paddingTopLeft: [ancho, 24], paddingBottomRight: [24, 24] };
  }

  // Dos anillos se abren desde un punto al llegar a él (una posición nueva o una elegida en la línea de tiempo).
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
        icon: icono(`${gris ? "marcador-acompanamiento--desactualizado" : "marcador-acompanamiento--vigilado"}${pide ? " marcador-acompanamiento--ayuda" : ""}`),
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

  // Siguiendo a una persona, los demás vigilados se desvanecen; en «todas», vuelven.
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
      const enlace = item.querySelector("[data-elegir-acompanamiento]");
      if (elegida) enlace?.setAttribute("aria-current", "true");
      else enlace?.removeAttribute("aria-current");
    }
  }

  // ---------- Recorrido de la sesión que se sigue ----------
  // Los puntos los da la ficha del modo («sesión») o el detalle («detalle»); la más reciente va primero.
  let capaRuta = null, claveRuta = "", ultimo = null;
  function dibujarRuta({ encuadrar = false } = {}) {
    const datos = modo === "detalle" ? regionDetalle?.querySelector("[data-recorrido]") : modo === "sesion" ? regionSesion.querySelector("[data-seguimiento]") : null;
    const texto = datos?.dataset.puntos ?? "";
    const actual = datos?.dataset.actual === "true";
    const clave = `${sesion}|${texto}|${actual}|${marcadores.has(sesion)}`;
    if (clave === claveRuta && !encuadrar) return; // nada cambió: el mapa no parpadea
    claveRuta = clave;
    capaRuta?.remove();
    capaRuta = null;
    if (!texto) { ultimo = null; return; }
    const puntos = texto.split(";").map((p) => p.split(",").map(Number));
    capaRuta = L.layerGroup().addTo(mapa);
    const linea = L.polyline([...puntos].reverse(), { color: colorRuta, weight: 5, lineJoin: "round", opacity: 0.9 }).addTo(capaRuta);
    if (puntos.length > 1) {
      L.marker(puntos.at(-1), {
        icon: L.divIcon({ className: "marcador-inicio-recorrido", html: "", iconSize: [14, 14], iconAnchor: [7, 7] }),
        title: "Inicio del recorrido", alt: "Inicio del recorrido", keyboard: false, interactive: false,
      }).addTo(capaRuta);
    }
    // La posición actual es el marcador de la sesión en el mapa; si no está en la lista (una sesión terminada), uno propio.
    if (!marcadores.has(sesion)) {
      const nombre = actual ? "Posición actual" : "Última posición conocida";
      L.marker(puntos[0], { icon: icono(actual ? "marcador-acompanamiento--vigilado" : "marcador-acompanamiento--desactualizado"), title: nombre, alt: nombre, keyboard: false })
        .addTo(capaRuta);
    }
    const punto = L.latLng(puntos[0]);
    const movido = ultimo !== null && !ultimo.equals(punto);
    ultimo = punto;
    if (encuadrar) {
      // Con una sola posición, los límites son ese punto: el mismo encuadre con margen la deja fuera del panel.
      const limites = puntos.length > 1 ? linea.getBounds() : L.latLngBounds([punto, punto]);
      mapa.flyToBounds(limites, { ...margenPanel(), maxZoom: 17, animate: !reducido.matches, duration: 0.8 });
      return;
    }
    // Llegó una posición nueva: el mapa la sigue solo si quedó fuera de la vista.
    if (movido) {
      if (!mapa.getBounds().contains(punto)) mapa.panTo(punto, { animate: !reducido.matches });
      anillo(punto, actual);
    }
  }

  // Una posición elegida en la línea de tiempo del detalle: el mapa va a ella y la señala.
  let elegida = null;
  function mostrar(lat, lon) {
    const punto = L.latLng(lat, lon);
    elegida?.remove();
    elegida = L.circleMarker(punto, { radius: 9, color: colorRuta, weight: 3, fillColor: "#FFFFFF", fillOpacity: 1, interactive: false }).addTo(mapa);
    mapa.flyToBounds(L.latLngBounds([punto, punto]), { ...margenPanel(), maxZoom: Math.max(mapa.getZoom(), 17), animate: !reducido.matches, duration: 0.6 });
    anillo(punto, true);
  }

  // ---------- Cambio de modo ----------
  function ponerModo(nuevo) {
    modo = nuevo;
    raiz.dataset.modo = nuevo;
    todas.hidden = nuevo !== "todas";
    regionSesion.hidden = nuevo !== "sesion";
    if (regionDetalle) regionDetalle.hidden = nuevo !== "detalle";
    panel.classList.toggle("vigilancia__panel--ancho", nuevo === "detalle");
    if (nuevo !== "detalle") {
      detalle?.detener();
      detalle = null;
      elegida?.remove();
      elegida = null;
    }
  }

  function iniciarDetalle() {
    detalle?.detener();
    detalle = window.CadDetalleAcompanamiento?.iniciar(regionDetalle, { alActualizar: () => dibujarRuta() }) ?? null;
  }

  // En pantallas estrechas el mapa va arriba del panel: se lleva a la vista para ver el recorrido aislado.
  function mapaALaVista() {
    if (getComputedStyle(panel).position !== "absolute")
      contenedor.scrollIntoView({ block: "start", behavior: reducido.matches ? "auto" : "smooth" });
  }

  const pedir = (url) => fetch(url, { credentials: "same-origin", headers: { Accept: "text/html" } }).catch(() => null);

  // Cada elección (o la vuelta a «todas») invalida las anteriores: si el operador elige A y enseguida B, la respuesta tardía
  // de A no reemplaza la ficha de B ni cambia la dirección.
  let eleccion = 0;
  async function elegir(id, { historial = true } = {}) {
    const turno = ++eleccion;
    const respuesta = await pedir(`/panel/acompanamientos?handler=Sesion&id=${encodeURIComponent(id)}`);
    if (turno !== eleccion) return;
    if (!respuesta || respuesta.redirected || !respuesta.ok) return; // la sesión ya no existe o terminó la sesión del panel
    const html = await respuesta.text();
    if (turno !== eleccion) return;
    const desdeDetalle = modo === "detalle" && sesion === id;
    regionSesion.innerHTML = html;
    if (!desdeDetalle) regionSesion.querySelector("[data-seguimiento]")?.classList.add("seguimiento--entra");
    if (sesion !== id) ultimo = null;
    sesion = id;
    ponerModo("sesion");
    if (historial) history.pushState({ sesion: id }, "", `/panel/acompanamientos?sesion=${encodeURIComponent(id)}`);
    aislar();
    dibujarRuta({ encuadrar: true });
    // Al volver del detalle, el foco vuelve a su enlace; al elegir a alguien, a su nombre.
    (desdeDetalle ? regionSesion.querySelector("[data-ver-detalle]") : regionSesion.querySelector("#seguimiento-titulo"))?.focus({ preventScroll: true });
    if (!desdeDetalle) mapaALaVista();
  }

  // El detalle completo: se trae la página del detalle y se pone solo su ficha en el panel, sobre el mismo mapa.
  async function abrirDetalle(id, { historial = true } = {}) {
    const turno = ++eleccion;
    const url = `/panel/acompanamientos/${encodeURIComponent(id)}`;
    const respuesta = await pedir(url);
    if (turno !== eleccion) return;
    // Sin respuesta útil (sin conexión, la sesión del panel terminó): la navegación normal lo resuelve.
    if (!respuesta || respuesta.redirected || !respuesta.ok) { location.assign(url); return; }
    const nuevo = new DOMParser().parseFromString(await respuesta.text(), "text/html");
    if (turno !== eleccion) return;
    const ficha = nuevo.querySelector("[data-region-detalle]");
    if (!ficha) { location.assign(url); return; }
    detalle?.detener();
    detalle = null;
    const importada = document.importNode(ficha, true);
    regionDetalle.replaceWith(importada);
    regionDetalle = importada;
    if (sesion !== id) ultimo = null;
    sesion = id;
    ponerModo("detalle");
    iniciarDetalle();
    if (historial) history.pushState({ sesion: id, detalle: true }, "", url);
    aislar();
    dibujarRuta({ encuadrar: true });
    regionDetalle.scrollTop = 0;
    regionDetalle.querySelector("#detalle-titulo")?.focus({ preventScroll: true });
  }

  function volver({ historial = true } = {}) {
    eleccion++; // una elección todavía en camino ya no vale
    if (sesion === null) return;
    const anterior = sesion;
    sesion = null;
    ultimo = null;
    ponerModo("todas");
    regionSesion.innerHTML = "";
    capaRuta?.remove();
    capaRuta = null;
    claveRuta = "";
    if (historial) history.pushState({}, "", "/panel/acompanamientos");
    aislar();
    const visibles = [...marcadores.values()].map((m) => m.getLatLng());
    if (visibles.length > 0) mapa.flyToBounds(L.latLngBounds(visibles), { ...margenPanel(), maxZoom: 15, animate: !reducido.matches, duration: 0.6 });
    region.querySelector(`[data-acompanamiento="${CSS.escape(anterior)}"] [data-elegir-acompanamiento]`)?.focus({ preventScroll: true });
  }

  // Un enlace con Ctrl, Cmd, Mayús o el botón central se abre como siempre (otra pestaña o ventana).
  const propio = (evento) => !(evento.ctrlKey || evento.metaKey || evento.shiftKey || evento.button !== 0);

  region.addEventListener("click", (evento) => {
    const enlace = evento.target.closest("[data-elegir-acompanamiento]");
    if (!enlace || !propio(evento)) return;
    evento.preventDefault();
    elegir(enlace.closest("[data-acompanamiento]").dataset.acompanamiento);
  });
  regionSesion.addEventListener("click", (evento) => {
    if (!propio(evento)) return;
    if (evento.target.closest("[data-volver-lista]")) {
      evento.preventDefault();
      volver();
    } else if (evento.target.closest("[data-ver-detalle]") && sesion !== null) {
      evento.preventDefault();
      abrirDetalle(sesion);
    }
  });
  // El detalle se cambia entero: sus clics se escuchan en el panel.
  panel.addEventListener("click", (evento) => {
    if (modo !== "detalle") return;
    const boton = evento.target.closest("[data-ver-posicion]");
    if (boton) {
      for (const fila of regionDetalle.querySelectorAll(".posicion--elegida")) fila.classList.remove("posicion--elegida");
      boton.closest("[data-posicion]")?.classList.add("posicion--elegida");
      mostrar(Number(boton.dataset.lat), Number(boton.dataset.lon));
      return;
    }
    if (evento.target.closest("[data-volver-seguimiento]") && propio(evento) && sesion !== null) {
      evento.preventDefault();
      elegir(sesion);
    }
  });
  document.addEventListener("keydown", (evento) => {
    if (evento.key !== "Escape" || sesion === null || evento.target.closest("input, textarea, select")) return;
    if (modo === "sesion") volver();
    // Del detalle se vuelve al seguimiento solo si la sesión sigue activa (una terminada viene del historial).
    else if (modo === "detalle" && regionDetalle.dataset.activa === "true") elegir(sesion);
  });
  window.addEventListener("popstate", () => {
    const enDetalle = location.pathname.match(/^\/panel\/acompanamientos\/([0-9a-f-]{36})$/i);
    const id = new URLSearchParams(location.search).get("sesion");
    if (enDetalle) abrirDetalle(enDetalle[1], { historial: false });
    else if (id) elegir(id, { historial: false });
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
      if (modo === "sesion") await actualizarSesion();
      else if (modo === "detalle") {
        await detalle?.refrescar();
        dibujarRuta(); // el marcador de la sesión pudo aparecer o irse con la lista
      }
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
    if (respuesta.redirected || !respuesta.ok || sesion !== id || modo !== "sesion") return;
    cambiarConFoco(regionSesion, await respuesta.text(), "[data-seguimiento]");
    dibujarRuta();
  }

  // Un aviso de acompañamiento que aparece en la lista suena una vez (sonidos.js, si están activados), y también lo que el
  // ciudadano informa por su cuenta cuando es una señal de peligro («Algo no está bien», «Necesito ayuda»). En el detalle la
  // lista llega después de cargar: lo que ya había no suena.
  const alertas = () => [...region.querySelectorAll("[data-aviso], [data-alerta-ciudadano]")].map((el) => el.dataset.aviso ?? el.dataset.alertaCiudadano);
  let avisosVistos = modo === "detalle" ? null : new Set(alertas());
  function avisarNuevos() {
    const actuales = alertas();
    if (avisosVistos === null) { avisosVistos = new Set(actuales); return; }
    let hayNuevo = false;
    for (const aviso of actuales) {
      if (avisosVistos.has(aviso)) continue;
      avisosVistos.add(aviso);
      hayNuevo = true;
    }
    if (hayNuevo) window.CadSonidos?.reproducir("aviso-acompanamiento");
  }

  dibujar();
  if (modo === "sesion") {
    history.replaceState({ sesion }, "", location.href);
    dibujarRuta({ encuadrar: true });
  } else if (modo === "detalle") {
    history.replaceState({ sesion, detalle: true }, "", location.href);
    iniciarDetalle();
    dibujarRuta({ encuadrar: true });
    actualizar(); // la lista no viene con el detalle: se pide para poder volver a «todas» sin recargar
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
