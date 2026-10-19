// HU11 — sección urgente de la central: SOS nuevos sin recargar, con aviso visual y sonoro.
// HU19 — serenos (estado, disponibilidad y última ubicación) en el mismo mapa, actualizados sin recargar.
// La base de datos es la fuente de verdad: ante cada aviso o reconexión se vuelve a pedir la lista
// al servidor (fragmento HTML generado por la página), en lugar de reconstruirla con datos del evento.
(() => {
  const region = document.querySelector("[data-region-lista]");
  const contador = document.querySelector("[data-contador]");
  const anuncio = document.querySelector("[data-anuncio]");
  const indicador = document.querySelector("[data-conexion]");
  const botonSonido = document.querySelector("[data-activar-sonido]");
  const seccion = document.querySelector(".central__urgente");
  const contenedorMapa = document.getElementById("mapa");
  // HU15-CA02 y HU20-CA07: reportes admitidos, en su propia sección; se seleccionan y asignan como un SOS.
  const regionReportes = document.querySelector("[data-region-reportes]");
  const contadorReportes = document.querySelector("[data-contador-reportes]");
  const casos = () => [...region.querySelectorAll("[data-sos]"), ...(regionReportes?.querySelectorAll("[data-sos]") ?? [])];
  // HU25: casos en curso con ubicación (no asignables), con su origen y su estado.
  const regionCasosMapa = document.querySelector("[data-region-casos-mapa]");
  const marcadoresEnCurso = [];
  const tituloBase = document.title;
  const avisoCancelacion = document.querySelector("[data-aviso-cancelacion]");
  let sinVer = 0;
  // SOS llegados en vivo que nadie ha abierto todavía: su tarjeta y su marcador quedan marcados (pulso).
  const nuevos = new Set();
  const reducido = matchMedia("(prefers-reduced-motion: reduce)");
  const SIRENA = '<svg class="icono" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M6 18v-5a6 6 0 0 1 12 0v5"/><path d="M4 18h16v3H4zM12 2v2M4.2 5.2l1.4 1.4M19.8 5.2l-1.4 1.4"/></svg>';
  const escudo = (disponible) => `<svg viewBox="0 0 32 36" aria-hidden="true" focusable="false"><path d="M16 2 30 8v9c0 9-6 15-14 17C8 32 2 26 2 17V8z" fill="${disponible ? "var(--mapa-sereno)" : "var(--mapa-sereno-no-disponible)"}" stroke="#fff" stroke-width="3" stroke-linejoin="round"/></svg>`;

  // ---------- Mapa base mínimo (Leaflet + OpenStreetMap) ----------
  // El zoom va abajo a la derecha: arriba a la izquierda lo taparía la cola de trabajo.
  const mapa = L.map(contenedorMapa, { zoomControl: false }).setView(
    [Number(contenedorMapa.dataset.centroLat), Number(contenedorMapa.dataset.centroLon)],
    Number(contenedorMapa.dataset.zoom));
  L.tileLayer(contenedorMapa.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
  const marcadores = new Map();

  // ---------- Vuelo al caso: el mapa viaja al punto, casi al zoom máximo, y lo marca al llegar ----------
  // Propósito: continuidad espacial (de la tarjeta al lugar). Con movimiento reducido el salto es inmediato.
  const ZOOM_CERCANO = 18;
  const DURACION_VUELO = 0.9; // segundos (Leaflet)

  // Franja horizontal del mapa que no tapan los paneles flotantes abiertos (en píxeles del contenedor).
  function franjaLibre() {
    const ancho = contenedorMapa.clientWidth;
    let izquierda = 0;
    let derecha = ancho;
    for (const panel of document.querySelectorAll("[data-panel]:not(.panel-flotante--plegado)")) {
      if (getComputedStyle(panel).position !== "absolute") continue; // por debajo de 760 px van bajo el mapa
      if (panel.offsetLeft + panel.offsetWidth / 2 < ancho / 2) izquierda = Math.max(izquierda, panel.offsetLeft + panel.offsetWidth);
      else derecha = Math.min(derecha, panel.offsetLeft);
    }
    return derecha - izquierda < 200 ? { izquierda: 0, derecha: ancho, ancho } : { izquierda, derecha, ancho };
  }

  // En pantallas estrechas el mapa queda encima de la lista: primero se trae a la vista.
  function traerMapaALaVista() {
    const caja = contenedorMapa.getBoundingClientRect();
    if (caja.top < 0 || caja.bottom > innerHeight)
      contenedorMapa.scrollIntoView({ block: "start", behavior: reducido.matches ? "auto" : "smooth" });
  }

  function volarA(marcador, { zoom = ZOOM_CERCANO, clase = "" } = {}) {
    const punto = marcador.getLatLng();
    const { izquierda, derecha, ancho } = franjaLibre();
    const desfase = (izquierda + derecha) / 2 - ancho / 2;
    const destino = mapa.unproject(mapa.project(punto, zoom).subtract([desfase, 0]), zoom);
    const llegar = () => { marcador.openPopup(); marcarLlegada(punto, clase); };
    traerMapaALaVista();
    if (reducido.matches) {
      mapa.setView(destino, zoom, { animate: false });
      llegar();
      return;
    }
    mapa.once("moveend", llegar);
    mapa.flyTo(destino, zoom, { duration: DURACION_VUELO, easeLinearity: 0.2 });
  }

  // El caso y el sereno sugerido en el mismo encuadre, sin quedar bajo los paneles.
  function encuadrar(limites, marcador, clase) {
    const { izquierda, derecha, ancho } = franjaLibre();
    const opciones = { paddingTopLeft: [izquierda + 48, 72], paddingBottomRight: [ancho - derecha + 48, 72], maxZoom: 17 };
    const llegar = () => { marcador?.openPopup(); if (marcador) marcarLlegada(marcador.getLatLng(), clase); };
    traerMapaALaVista();
    if (reducido.matches) {
      mapa.fitBounds(limites, { ...opciones, animate: false });
      llegar();
      return;
    }
    mapa.once("moveend", llegar);
    mapa.flyToBounds(limites, { ...opciones, duration: DURACION_VUELO, easeLinearity: 0.2 });
  }

  // Dos anillos que se abren desde el punto al aterrizar; se retiran solos.
  let llegada = null;
  function marcarLlegada(punto, clase) {
    if (reducido.matches) return;
    llegada?.remove();
    const anillo = L.marker(punto, {
      icon: L.divIcon({ className: `llegada ${clase}`, html: "<span></span><span></span>", iconSize: [96, 96], iconAnchor: [48, 48] }),
      interactive: false,
      keyboard: false,
      zIndexOffset: -1000,
    }).addTo(mapa);
    llegada = anillo;
    setTimeout(() => { anillo.remove(); if (llegada === anillo) llegada = null; }, 1300);
  }

  // El marcador elegido queda «fijado»: un anillo se cierra sobre él y pasa por encima de los demás.
  function resaltar(conjunto, id) {
    for (const [clave, marcador] of conjunto) {
      const elegido = clave === id;
      marcador.getElement()?.classList.toggle("marcador--elegido", elegido);
      marcador.setZIndexOffset(elegido ? 1000 : 0);
    }
  }
  const claseLlegada = (id) => document.getElementById(`sos-${id}`)?.dataset.origen === "Reporte" ? "llegada--reporte" : "";

  function dibujarMarcadores() {
    for (const marcador of marcadores.values()) marcador.remove();
    marcadores.clear();
    for (const item of casos()) {
      if (!item.dataset.lat) continue;
      const id = item.dataset.sos;
      if (item.dataset.origen === "Reporte") {
        const texto = `Reporte admitido de ${item.dataset.ciudadano}`;
        const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
          icon: L.divIcon({ className: "marcador-reporte", html: "", iconSize: [26, 26], iconAnchor: [13, 13], popupAnchor: [0, -18] }),
          title: texto,
          alt: texto,
          keyboard: true,
        })
          .bindPopup(`<strong>Reporte · Pendiente</strong><br>Identificador: ${escapar(id)}<br>${escapar(item.dataset.ciudadano)}<br>Tipo: ${escapar(item.dataset.categoria)}<br>Lugar del hecho indicado por el ciudadano`)
          .on("click", () => seleccionar(id, { desdeMapa: true }))
          .addTo(mapa);
        marcadores.set(id, marcador);
        continue;
      }
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon({ className: `marcador-sos${nuevos.has(id) ? " marcador-sos--pulso" : ""}`, html: SIRENA, iconSize: [36, 32], iconAnchor: [18, 16], popupAnchor: [0, -22] }),
        title: `SOS de ${item.dataset.ciudadano}`,
        alt: `SOS de ${item.dataset.ciudadano}`,
        keyboard: true,
      })
        .bindPopup(`<strong>SOS · Pendiente</strong><br>Identificador: ${escapar(id)}<br>${escapar(item.dataset.ciudadano)}<br>Precisión ± ${escapar(item.dataset.precision)} m`)
        .on("click", () => seleccionar(id, { desdeMapa: true }))
        .addTo(mapa);
      marcadores.set(id, marcador);
    }
  }

  let sosSeleccionado = null;

  function seleccionar(id, { desdeMapa = false, conSereno = false } = {}) {
    sosSeleccionado = id;
    for (const item of document.querySelectorAll(".sos--seleccionado")) item.classList.remove("sos--seleccionado");
    const item = document.getElementById(`sos-${id}`);
    if (nuevos.delete(id)) marcadores.get(id)?.getElement()?.classList.remove("marcador-sos--pulso");
    if (item) {
      item.classList.add("sos--seleccionado");
      item.classList.remove("sos--nuevo");
      if (desdeMapa) {
        item.scrollIntoView({ block: "nearest" });
        item.focus();
      }
    }
    const marcador = marcadores.get(id);
    resaltar(marcadores, id);
    marcarComoVisto();
    actualizarAsignacion();
    dibujarDespacho();
    if (!marcador || desdeMapa) return;
    // Al elegir sereno se encuadran el caso y el sereno más cercano; al «Ver en el mapa», se vuela al caso.
    if (conSereno && lineaDespacho) encuadrar(lineaDespacho.getBounds(), marcador, claseLlegada(id));
    else volarA(marcador, { clase: claseLlegada(id) });
  }

  // ---------- Línea de despacho: del caso elegido al sereno disponible más cercano ----------
  // Ayuda a decidir; no asigna nada. La distancia es en línea recta.
  let lineaDespacho = null;
  function dibujarDespacho() {
    lineaDespacho?.remove();
    lineaDespacho = null;
    for (const el of regionSerenos.querySelectorAll(".sereno--sugerido")) el.classList.remove("sereno--sugerido");
    for (const el of regionSerenos.querySelectorAll("[data-distancia]")) el.remove();
    const caso = sosSeleccionado && document.getElementById(`sos-${sosSeleccionado}`);
    if (!caso?.dataset.lat) return;
    const origen = L.latLng(Number(caso.dataset.lat), Number(caso.dataset.lon));
    let cercano = null;
    for (const s of regionSerenos.querySelectorAll('[data-sereno][data-disponible="true"]')) {
      if (!s.dataset.lat) continue;
      const destino = L.latLng(Number(s.dataset.lat), Number(s.dataset.lon));
      const metros = origen.distanceTo(destino);
      if (!cercano || metros < cercano.metros) cercano = { s, destino, metros };
    }
    if (!cercano) return;
    const texto = cercano.metros < 1000 ? `${Math.round(cercano.metros)} m` : `${(cercano.metros / 1000).toFixed(1).replace(".", ",")} km`;
    const color = getComputedStyle(document.documentElement).getPropertyValue("--color-primario").trim() || "#1D4ED8";
    lineaDespacho = L.polyline([origen, cercano.destino], { color, weight: 3, dashArray: "8 7", interactive: false, className: "linea-despacho" })
      .bindTooltip(texto, { permanent: true, direction: "center", className: "etiqueta-despacho" })
      .addTo(mapa);
    cercano.s.classList.add("sereno--sugerido");
    cercano.s.querySelector(".sereno__cabecera")?.insertAdjacentHTML("afterend",
      `<p class="sereno__distancia" data-distancia>Más cercano al caso elegido · ${texto}</p>`);
  }

  // HU25-CA01/CA02/CA03: un marcador por caso en curso, distinto por origen (color) y por estado (con borde; reporte en
  // revisión con borde discontinuo). Al elegirlo se ven su identificador, origen y estado, con un enlace a su detalle.
  function dibujarCasosEnCurso() {
    for (const m of marcadoresEnCurso.splice(0)) m.remove();
    for (const item of regionCasosMapa?.querySelectorAll("[data-caso-mapa]") ?? []) {
      const reporte = item.dataset.origen === "Reporte";
      const revision = item.dataset.estado === "Recibido" || item.dataset.estado === "En revisión";
      const clase = `${reporte ? "marcador-reporte" : "marcador-sos"} marcador-caso--en-curso${revision ? " marcador-caso--revision" : ""}`;
      const texto = `${item.dataset.origen} · ${item.dataset.estado} · ${item.dataset.ciudadano}`;
      marcadoresEnCurso.push(L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon({ className: clase, html: reporte ? "" : SIRENA, iconSize: reporte ? [26, 26] : [36, 32], iconAnchor: reporte ? [13, 13] : [18, 16], popupAnchor: [0, reporte ? -18 : -22] }),
        title: texto,
        alt: texto,
        keyboard: true,
      })
        .bindPopup(`<strong>${escapar(item.dataset.origen)} · ${escapar(item.dataset.estado)}</strong><br>Identificador: ${escapar(item.dataset.casoMapa)}<br>${escapar(item.dataset.ciudadano)}<br><a href="${escapar(item.dataset.detalle)}">Ver el caso</a>`)
        .addTo(mapa));
    }
  }

  function alHacerClicEnLista(evento) {
    const boton = evento.target.closest("[data-ver-en-mapa]");
    if (boton) seleccionar(boton.closest("[data-sos]").dataset.sos);
    // HU20: elegir sereno para un SOS (también sin ubicación): se selecciona y se va a los serenos.
    const elegir = evento.target.closest("[data-seleccionar-sos]");
    if (elegir) {
      desplegar(document.getElementById("panel-serenos"));
      seleccionar(elegir.closest("[data-sos]").dataset.sos, { conSereno: true });
      const primero = regionSerenos.querySelector("[data-asignar-sereno]:not([hidden])");
      (primero ?? regionSerenos).scrollIntoView({ block: "center" });
      primero?.focus();
    }
  }
  region.addEventListener("click", alHacerClicEnLista);
  regionReportes?.addEventListener("click", alHacerClicEnLista);

  // ---------- HU19: serenos en el mismo mapa (sin rutas, líneas ni distancias) ----------
  const regionSerenos = document.querySelector("[data-region-serenos]");
  const marcadoresSerenos = new Map();

  function dibujarSerenos() {
    for (const marcador of marcadoresSerenos.values()) marcador.remove();
    marcadoresSerenos.clear();
    for (const item of regionSerenos.querySelectorAll("[data-sereno][data-lat]")) {
      if (!item.dataset.lat) continue; // HU19-CA04: sin ubicación no hay marcador
      const id = item.dataset.sereno;
      const disponible = item.dataset.disponible === "true";
      const texto = `Sereno ${item.dataset.nombre}, ${disponible ? "disponible" : "no disponible"}`;
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon({
          className: `marcador-sereno${disponible ? "" : " marcador-sereno--no-disponible"}`,
          html: escudo(disponible), iconSize: [30, 34], iconAnchor: [15, 17], popupAnchor: [0, -22],
        }),
        title: texto,
        alt: texto,
        keyboard: true,
      })
        .bindPopup(`<strong>Sereno · Operativo</strong><br>${escapar(item.dataset.nombre)}<br>${disponible ? "Disponible" : "No disponible"}<br>Última ubicación a las ${escapar(item.dataset.hora)}`)
        .on("click", () => seleccionarSereno(id, { desdeMapa: true }))
        .addTo(mapa);
      marcadoresSerenos.set(id, marcador);
    }
  }

  function seleccionarSereno(id, { desdeMapa = false } = {}) {
    for (const item of regionSerenos.querySelectorAll(".sereno--seleccionado")) item.classList.remove("sereno--seleccionado");
    const item = document.getElementById(`sereno-${id}`);
    if (item) {
      item.classList.add("sereno--seleccionado");
      if (desdeMapa) {
        item.scrollIntoView({ block: "nearest" });
        item.focus();
      }
    }
    const marcador = marcadoresSerenos.get(id);
    resaltar(marcadoresSerenos, id);
    if (marcador && !desdeMapa) volarA(marcador, { zoom: 17, clase: "llegada--sereno" });
  }

  regionSerenos.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-ver-sereno]");
    if (boton) seleccionarSereno(boton.closest("[data-sereno]").dataset.sereno);
    const asignar = evento.target.closest("[data-asignar-sereno]");
    if (asignar) abrirConfirmacion(asignar.closest("[data-sereno]"));
  });

  // ---------- HU20: asignar el SOS seleccionado a un sereno disponible ----------
  // El operador compara las ubicaciones en el mapa y elige; el panel solo pide confirmación.
  const confirmacion = document.querySelector("[data-confirmar-asignacion]");
  const textoConfirmacion = confirmacion.querySelector("[data-texto-confirmacion]");

  function actualizarAsignacion() {
    const sos = sosSeleccionado && document.getElementById(`sos-${sosSeleccionado}`);
    if (!sos) {
      sosSeleccionado = null;
      confirmacion.hidden = true;
    }
    for (const boton of regionSerenos.querySelectorAll("[data-asignar-sereno]")) boton.hidden = !sos;
  }

  function abrirConfirmacion(itemSereno) {
    const sos = document.getElementById(`sos-${sosSeleccionado}`);
    if (!sos || !itemSereno) return;
    const esReporte = sos.dataset.origen === "Reporte";
    confirmacion.action = esReporte
      ? `/panel/reportes/${sosSeleccionado}?handler=Asignar`
      : `/panel/central/sos/${sosSeleccionado}?handler=Asignar`;
    confirmacion.querySelector("[data-campo-sereno]").value = itemSereno.dataset.sereno;
    // Un identificador por confirmación: un reenvío no crea una segunda asignación (CA08).
    confirmacion.querySelector("[data-campo-solicitud]").value = crypto.randomUUID();
    const hora = sos.querySelector("time")?.textContent ?? "";
    textoConfirmacion.textContent = `¿Asignar el ${esReporte ? "reporte" : "SOS"} de ${sos.dataset.ciudadano} (${hora}) a ${itemSereno.dataset.nombre}?`;
    origenDialogo = document.activeElement;
    confirmacion.hidden = false;
    confirmacion.querySelector("button[type=submit]").focus();
  }

  let origenDialogo = null;
  function cerrarConfirmacion() {
    confirmacion.hidden = true;
    origenDialogo?.focus?.();
  }
  confirmacion.querySelector("[data-cancelar-asignacion]").addEventListener("click", cerrarConfirmacion);
  confirmacion.addEventListener("click", (evento) => { if (evento.target === confirmacion) cerrarConfirmacion(); });
  confirmacion.addEventListener("keydown", (evento) => {
    if (evento.key === "Escape") { evento.preventDefault(); cerrarConfirmacion(); return; }
    if (evento.key !== "Tab") return;
    const enfocables = [...confirmacion.querySelectorAll("button")];
    const primero = enfocables[0];
    const ultimo = enfocables.at(-1);
    if (evento.shiftKey && document.activeElement === primero) { evento.preventDefault(); ultimo.focus(); }
    else if (!evento.shiftKey && document.activeElement === ultimo) { evento.preventDefault(); primero.focus(); }
  });

  // ---------- Paneles plegables y capas del mapa ----------
  function desplegar(panel) {
    if (!panel?.classList.contains("panel-flotante--plegado")) return;
    panel.classList.remove("panel-flotante--plegado");
    panel.querySelector("[data-plegar]").setAttribute("aria-expanded", "true");
  }
  for (const boton of document.querySelectorAll("[data-plegar]")) {
    boton.addEventListener("click", () => {
      const plegado = boton.closest("[data-panel]").classList.toggle("panel-flotante--plegado");
      boton.setAttribute("aria-expanded", String(!plegado));
    });
  }
  const central = document.querySelector(".central");
  for (const boton of document.querySelectorAll("[data-capa]")) {
    boton.addEventListener("click", () => {
      const visible = boton.getAttribute("aria-pressed") !== "true";
      boton.setAttribute("aria-pressed", String(visible));
      central.classList.toggle(`central--sin-${boton.dataset.capa}`, !visible);
      if (boton.dataset.capa === "sereno") lineaDespacho?.getElement()?.classList.toggle("visualmente-oculto", !visible);
    });
  }

  // HU19-CA03: se vuelve a pedir el fragmento al servidor (fuente de verdad); se conserva la selección.
  async function refrescarSerenos() {
    const respuesta = await fetch("/panel/central?handler=Serenos", { headers: { Accept: "text/html" }, credentials: "same-origin" });
    if (respuesta.redirected || !respuesta.ok) return false;
    const seleccionado = regionSerenos.querySelector(".sereno--seleccionado")?.dataset.sereno;
    regionSerenos.innerHTML = await respuesta.text();
    dibujarSerenos();
    if (seleccionado) {
      document.getElementById(`sereno-${seleccionado}`)?.classList.add("sereno--seleccionado");
      resaltar(marcadoresSerenos, seleccionado);
    }
    actualizarAsignacion();
    dibujarDespacho();
    return true;
  }

  // ---------- Aviso visual y sonoro ----------
  let audio = null;

  botonSonido.addEventListener("click", () => {
    // Los navegadores solo permiten sonido tras una acción del usuario (Plan de Arquitectura, SP-E).
    audio ??= new AudioContext();
    audio.resume();
    botonSonido.setAttribute("aria-pressed", "true");
    botonSonido.textContent = "Avisos sonoros activados";
    botonSonido.disabled = true;
    tono([660], 0.12);
  });

  function tono(frecuencias, duracion) {
    if (!audio || audio.state !== "running") return false;
    let inicio = audio.currentTime;
    for (const frecuencia of frecuencias) {
      const oscilador = audio.createOscillator();
      const volumen = audio.createGain();
      oscilador.type = "square";
      oscilador.frequency.value = frecuencia;
      volumen.gain.setValueAtTime(0.15, inicio);
      volumen.gain.exponentialRampToValueAtTime(0.001, inicio + duracion);
      oscilador.connect(volumen).connect(audio.destination);
      oscilador.start(inicio);
      oscilador.stop(inicio + duracion);
      inicio += duracion + 0.08;
    }
    return true;
  }

  function avisar(id) {
    const item = document.getElementById(`sos-${id}`);
    if (!item) return;
    nuevos.add(id);
    item.classList.add("sos--nuevo");
    marcadores.get(id)?.getElement()?.classList.add("marcador-sos--pulso");
    sinVer += 1;
    document.title = `(${sinVer}) ${tituloBase}`;
    const ubicacion = item.dataset.lat ? "con ubicación" : "sin ubicación disponible";
    anuncio.textContent = `Nuevo SOS de ${item.dataset.ciudadano}, ${ubicacion}.`;
    if (tono([880, 660, 880], 0.18))
      seccion.dataset.avisosSonoros = String(Number(seccion.dataset.avisosSonoros) + 1);
  }

  // HU09-CA02: el titular canceló un SOS pendiente. Sale de la lista (resincronización) y se anuncia con un aviso visible
  // y, si los avisos sonoros están activados, un tono distinto del de un SOS nuevo.
  async function avisarCancelacion(id) {
    const ciudadano = document.getElementById(`sos-${id}`)?.dataset.ciudadano;
    // Si estaba seleccionado, la resincronización cierra su confirmación de asignación (actualizarAsignacion).
    if (!(await refrescar())) return;
    const texto = ciudadano ? `${ciudadano} canceló su SOS. Ya no está pendiente.` : "Un ciudadano canceló su SOS. Ya no está pendiente.";
    avisoCancelacion.querySelector("[data-texto]").textContent = texto;
    avisoCancelacion.querySelector("a").href = `/panel/casos/${id}/historial`;
    avisoCancelacion.hidden = false;
    avisoCancelacion.dataset.casoId = id;
    anuncio.textContent = texto;
    if (tono([440, 330], 0.2))
      seccion.dataset.avisosSonoros = String(Number(seccion.dataset.avisosSonoros) + 1);
  }

  function marcarComoVisto() {
    sinVer = 0;
    document.title = tituloBase;
  }
  window.addEventListener("focus", marcarComoVisto);

  // ---------- Resincronización desde la base de datos ----------
  async function refrescar() {
    const respuesta = await fetch("/panel/central?handler=Lista", { headers: { Accept: "text/html" }, credentials: "same-origin" });
    if (respuesta.redirected || !respuesta.ok) {
      estadoConexion("desconectado", "La sesión terminó o no hay acceso. Vuelve a ingresar para recibir SOS.");
      return false;
    }
    const antes = posiciones(region);
    region.innerHTML = await respuesta.text();
    for (const id of nuevos) document.getElementById(`sos-${id}`)?.classList.add("sos--nuevo");
    moverEnSuSitio(region, antes);
    contador.textContent = region.querySelector("[data-lista-sos]").dataset.total;
    if (regionReportes) {
      const reportes = await fetch("/panel/central?handler=Reportes", { headers: { Accept: "text/html" }, credentials: "same-origin" });
      if (reportes.ok && !reportes.redirected) {
        regionReportes.innerHTML = await reportes.text();
        contadorReportes.textContent = regionReportes.querySelector("[data-lista-reportes]").dataset.total;
      }
    }
    if (regionCasosMapa) {
      const enCurso = await fetch("/panel/central?handler=CasosMapa", { headers: { Accept: "text/html" }, credentials: "same-origin" });
      if (enCurso.ok && !enCurso.redirected) regionCasosMapa.innerHTML = await enCurso.text();
    }
    dibujarMarcadores();
    dibujarCasosEnCurso();
    if (sosSeleccionado) {
      document.getElementById(`sos-${sosSeleccionado}`)?.classList.add("sos--seleccionado");
      resaltar(marcadores, sosSeleccionado);
    }
    actualizarAsignacion();
    return true;
  }

  // Lo que cambia se mueve en su sitio: cada tarjeta que sigue en la lista se desliza desde su posición anterior.
  function posiciones(contenedor) {
    return new Map([...contenedor.querySelectorAll("[data-sos]")].map((el) => [el.dataset.sos, el.getBoundingClientRect().top]));
  }
  function moverEnSuSitio(contenedor, antes) {
    if (reducido.matches) return;
    for (const el of contenedor.querySelectorAll("[data-sos]")) {
      const previo = antes.get(el.dataset.sos);
      if (previo === undefined) continue;
      const desplazamiento = previo - el.getBoundingClientRect().top;
      if (Math.abs(desplazamiento) < 1) continue;
      el.animate([{ transform: `translateY(${desplazamiento}px)` }, { transform: "none" }],
        { duration: 240, easing: "cubic-bezier(0.77, 0, 0.175, 1)" });
    }
  }

  // ---------- Conexión en tiempo real ----------
  function estadoConexion(estado, texto) {
    indicador.className = `conexion conexion--${estado}`;
    indicador.dataset.estado = estado;
    indicador.querySelector("[data-texto]").textContent = texto;
  }

  const conexion = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/central")
    .withAutomaticReconnect([0, 2000, 5000, 10000, 15000])
    .build();

  conexion.on("SosRecibido", async ({ id }) => {
    if (!(await refrescar())) return;
    avisar(id);
    // RNF02 (solo con la medición activada): acuse cuando el SOS ya se muestra y se ha avisado.
    if (seccion.dataset.medicion === "true" && document.getElementById(`sos-${id}`))
      conexion.invoke("ConfirmarPresentacion", id).catch(() => {});
  });
  conexion.on("SerenoActualizado", () => refrescarSerenos());
  conexion.on("SosCancelado", ({ id }) => avisarCancelacion(id));
  // HU20: un caso cambió de estado (p. ej., asignado por otro operador): deja la sección urgente.
  conexion.on("CasoActualizado", () => refrescar());
  conexion.onreconnecting(() => estadoConexion("reconectando", "Reconectando… La lista puede no estar actualizada."));
  conexion.onreconnected(async () => {
    // Los avisos emitidos durante el corte se recuperan consultando la base de datos.
    if (await refrescar()) estadoConexion("conectado", "Conectado en tiempo real");
    await refrescarSerenos();
  });
  conexion.onclose(() => {
    estadoConexion("desconectado", "Sin conexión en tiempo real. Reintentando…");
    setTimeout(iniciar, 5000);
  });

  async function iniciar() {
    try {
      await conexion.start();
      if (await refrescar()) estadoConexion("conectado", "Conectado en tiempo real");
      await refrescarSerenos();
    } catch {
      estadoConexion("desconectado", "Sin conexión en tiempo real. Reintentando…");
      setTimeout(iniciar, 5000);
    }
  }

  function escapar(texto) {
    return String(texto ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
  }

  dibujarMarcadores();
  dibujarCasosEnCurso();
  dibujarSerenos();
  iniciar();

  // Acceso para la verificación automatizada (estado de la conexión y vista del mapa, sin datos).
  window.cadCentral = { conexion, refrescar, refrescarSerenos, mapa };
})();
