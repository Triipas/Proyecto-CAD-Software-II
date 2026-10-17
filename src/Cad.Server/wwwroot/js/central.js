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
  let sinVer = 0;

  // ---------- Mapa base mínimo (Leaflet + OpenStreetMap) ----------
  const mapa = L.map(contenedorMapa, { zoomControl: true }).setView(
    [Number(contenedorMapa.dataset.centroLat), Number(contenedorMapa.dataset.centroLon)],
    Number(contenedorMapa.dataset.zoom));
  L.tileLayer(contenedorMapa.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  const marcadores = new Map();

  function dibujarMarcadores() {
    for (const marcador of marcadores.values()) marcador.remove();
    marcadores.clear();
    for (const item of casos()) {
      if (!item.dataset.lat) continue;
      const id = item.dataset.sos;
      if (item.dataset.origen === "Reporte") {
        const texto = `Reporte admitido de ${item.dataset.ciudadano}`;
        const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
          icon: L.divIcon({ className: "marcador-reporte", html: "<span>R</span>", iconSize: [30, 30], iconAnchor: [15, 15] }),
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
        icon: L.divIcon({ className: "marcador-sos", html: "<span>SOS</span>", iconSize: [44, 28], iconAnchor: [22, 28] }),
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

  function seleccionar(id, { desdeMapa = false } = {}) {
    sosSeleccionado = id;
    for (const item of document.querySelectorAll(".sos--seleccionado")) item.classList.remove("sos--seleccionado");
    const item = document.getElementById(`sos-${id}`);
    if (item) {
      item.classList.add("sos--seleccionado");
      item.classList.remove("sos--nuevo");
      if (desdeMapa) {
        item.scrollIntoView({ block: "nearest" });
        item.focus();
      }
    }
    const marcador = marcadores.get(id);
    if (marcador && !desdeMapa) {
      mapa.setView(marcador.getLatLng(), Math.max(mapa.getZoom(), 15));
      marcador.openPopup();
    }
    marcarComoVisto();
    actualizarAsignacion();
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
        icon: L.divIcon({ className: clase, html: `<span>${reporte ? "R" : "SOS"}</span>`, iconSize: reporte ? [30, 30] : [44, 28], iconAnchor: reporte ? [15, 15] : [22, 28] }),
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
      seleccionar(elegir.closest("[data-sos]").dataset.sos);
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
          html: "<span>S</span>", iconSize: [30, 30], iconAnchor: [15, 15],
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
    if (marcador && !desdeMapa) {
      mapa.setView(marcador.getLatLng(), Math.max(mapa.getZoom(), 15));
      marcador.openPopup();
    }
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
    confirmacion.hidden = false;
    confirmacion.querySelector("button[type=submit]").focus();
  }

  confirmacion.querySelector("[data-cancelar-asignacion]").addEventListener("click", () => {
    confirmacion.hidden = true;
  });

  // HU19-CA03: se vuelve a pedir el fragmento al servidor (fuente de verdad); se conserva la selección.
  async function refrescarSerenos() {
    const respuesta = await fetch("/panel/central?handler=Serenos", { headers: { Accept: "text/html" }, credentials: "same-origin" });
    if (respuesta.redirected || !respuesta.ok) return false;
    const seleccionado = regionSerenos.querySelector(".sereno--seleccionado")?.dataset.sereno;
    regionSerenos.innerHTML = await respuesta.text();
    dibujarSerenos();
    if (seleccionado) document.getElementById(`sereno-${seleccionado}`)?.classList.add("sereno--seleccionado");
    actualizarAsignacion();
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
    item.classList.add("sos--nuevo");
    sinVer += 1;
    document.title = `(${sinVer}) ${tituloBase}`;
    const ubicacion = item.dataset.lat ? "con ubicación" : "sin ubicación disponible";
    anuncio.textContent = `Nuevo SOS de ${item.dataset.ciudadano}, ${ubicacion}.`;
    if (tono([880, 660, 880], 0.18))
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
    region.innerHTML = await respuesta.text();
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
    if (sosSeleccionado) document.getElementById(`sos-${sosSeleccionado}`)?.classList.add("sos--seleccionado");
    actualizarAsignacion();
    return true;
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

  // Acceso para la verificación automatizada (estado de la conexión, sin datos).
  window.cadCentral = { conexion, refrescar, refrescarSerenos };
})();
