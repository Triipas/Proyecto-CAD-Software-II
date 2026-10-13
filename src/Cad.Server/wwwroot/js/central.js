// HU11 — sección urgente de la central: SOS nuevos sin recargar, con aviso visual y sonoro.
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
    for (const item of region.querySelectorAll("[data-sos][data-lat]")) {
      if (!item.dataset.lat) continue;
      const id = item.dataset.sos;
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon({ className: "marcador-sos", html: "<span>SOS</span>", iconSize: [44, 28], iconAnchor: [22, 28] }),
        title: `SOS de ${item.dataset.ciudadano}`,
        alt: `SOS de ${item.dataset.ciudadano}`,
        keyboard: true,
      })
        .bindPopup(`<strong>SOS · Pendiente</strong><br>${escapar(item.dataset.ciudadano)}<br>Precisión ± ${escapar(item.dataset.precision)} m`)
        .on("click", () => seleccionar(id, { desdeMapa: true }))
        .addTo(mapa);
      marcadores.set(id, marcador);
    }
  }

  function seleccionar(id, { desdeMapa = false } = {}) {
    for (const item of region.querySelectorAll(".sos--seleccionado")) item.classList.remove("sos--seleccionado");
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
  }

  region.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-ver-en-mapa]");
    if (boton) seleccionar(boton.closest("[data-sos]").dataset.sos);
  });

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
    dibujarMarcadores();
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
    if (await refrescar()) avisar(id);
  });
  conexion.onreconnecting(() => estadoConexion("reconectando", "Reconectando… La lista puede no estar actualizada."));
  conexion.onreconnected(async () => {
    // Los avisos emitidos durante el corte se recuperan consultando la base de datos.
    if (await refrescar()) estadoConexion("conectado", "Conectado en tiempo real");
  });
  conexion.onclose(() => {
    estadoConexion("desconectado", "Sin conexión en tiempo real. Reintentando…");
    setTimeout(iniciar, 5000);
  });

  async function iniciar() {
    try {
      await conexion.start();
      if (await refrescar()) estadoConexion("conectado", "Conectado en tiempo real");
    } catch {
      estadoConexion("desconectado", "Sin conexión en tiempo real. Reintentando…");
      setTimeout(iniciar, 5000);
    }
  }

  function escapar(texto) {
    return String(texto ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
  }

  dibujarMarcadores();
  iniciar();

  // Acceso para la verificación automatizada (estado de la conexión, sin datos).
  window.cadCentral = { conexion, refrescar };
})();
