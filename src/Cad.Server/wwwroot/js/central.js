// HU11 — sección urgente de la central: SOS nuevos sin recargar, con aviso visual y sonoro.
// HU19 — serenos (estado, disponibilidad y última ubicación) en el mismo mapa, actualizados sin recargar.
// La base de datos es la fuente de verdad: ante cada aviso o reconexión se vuelve a pedir la lista
// al servidor (fragmento HTML generado por la página), en lugar de reconstruirla con datos del evento.
(() => {
  const region = document.querySelector("[data-region-lista]");
  const contador = document.querySelector("[data-contador]");
  const anuncio = document.querySelector("[data-anuncio]");
  const indicador = document.querySelector("[data-conexion]");
  const seccion = document.querySelector(".central__urgente");
  const contenedorMapa = document.getElementById("mapa");
  // HU15-CA02 y HU20-CA07: reportes admitidos, en su propia sección; se seleccionan y asignan como un SOS.
  const regionReportes = document.querySelector("[data-region-reportes]");
  const contadorReportes = document.querySelector("[data-contador-reportes]");
  const casos = () => [...region.querySelectorAll("[data-sos]"), ...(regionReportes?.querySelectorAll("[data-sos]") ?? [])];
  // HU25: casos en curso con ubicación (no asignables), con su origen y su estado.
  const regionCasosMapa = document.querySelector("[data-region-casos-mapa]");
  const marcadoresEnCurso = new Map();
  const tituloBase = document.title;
  const avisoCancelacion = document.querySelector("[data-aviso-cancelacion]");
  let sinVer = 0;
  // SOS llegados en vivo que nadie ha abierto todavía: su tarjeta y su marcador quedan marcados (pulso).
  const nuevos = new Set();
  const reducido = matchMedia("(prefers-reduced-motion: reduce)");
  // Los marcadores (forma, icono y ancla) los define marcadores.js, comunes con los mapas de detalle.
  const { SIRENA, DOCUMENTO, escudo } = window.cadMarcadores;

  // ---------- Mapa base mínimo (Leaflet + OpenStreetMap) ----------
  // El zoom va abajo a la derecha: arriba a la izquierda lo taparía la cola de trabajo.
  // Sin la marca «Leaflet» (su licencia BSD-2 no la exige en pantalla); el crédito de OpenStreetMap sí se mantiene.
  L.Control.Attribution.prototype.options.prefix = false;
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
  // Al elegir un caso sin sereno sugerido, el mapa acerca hasta el zoom máximo de las teselas (24/10/2026).
  const ZOOM_CERCANO = 19;
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

  // Centro de la franja del mapa que no tapan los paneles, para un punto y un zoom.
  function centroLibre(punto, zoom) {
    const { izquierda, derecha, ancho } = franjaLibre();
    const desfase = (izquierda + derecha) / 2 - ancho / 2;
    return mapa.unproject(mapa.project(punto, zoom).subtract([desfase, 0]), zoom);
  }

  // Al tocar un caso en el mapa se centra en la franja libre, sin cambiar el zoom (24/10/2026).
  // En el cuadro siguiente: el globo se abre después de este clic y su ajuste automático cortaba el centrado (27/10/2026).
  function centrar(marcador) {
    requestAnimationFrame(() => mapa.panTo(centroLibre(marcador.getLatLng(), mapa.getZoom()), { animate: !reducido.matches, duration: 0.5 }));
  }

  function volarA(marcador, { zoom = ZOOM_CERCANO, clase = "" } = {}) {
    const punto = marcador.getLatLng();
    const destino = centroLibre(punto, zoom);
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

  // Fase 1 de movimiento (26/10/2026): los marcadores ya no se rehacen en cada aviso. El que sigue se queda (y se desliza si
  // cambió de posición), el que llega entra creciendo y el que se va se desvanece. Así el mapa no parpadea con cada aviso.
  let mapaDibujado = false;
  const { deslizarMarcador: deslizar, retirarMarcador: retirar } = cadMovimiento;
  // En la carga inicial no se anima: solo lo que llega con la central abierta.
  const entrar = (marcador) => { if (mapaDibujado) cadMovimiento.entrarMarcador(marcador); };

  function dibujarMarcadores() {
    const vigentes = new Set();
    for (const item of casos()) {
      if (!item.dataset.lat) continue;
      const id = item.dataset.sos;
      vigentes.add(id);
      const existente = marcadores.get(id);
      if (existente) {
        deslizar(existente, [Number(item.dataset.lat), Number(item.dataset.lon)]);
        existente.getElement()?.classList.toggle("marcador-sos--pulso", nuevos.has(id));
        continue;
      }
      // El detalle se arma al abrirlo con la tarjeta vigente (la del momento, no la de cuando se creó el marcador).
      const tarjeta = () => document.getElementById(`sos-${id}`) ?? item;
      if (item.dataset.origen === "Reporte") {
        const texto = `Reporte admitido de ${item.dataset.ciudadano}`;
        const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
          icon: L.divIcon(cadMarcadores.caso(true)),
          title: texto,
          alt: texto,
          keyboard: true,
        })
          // Primero se elige el caso y después se abre el detalle: así ya muestra el sereno sugerido.
          .on("click", () => elegirCaso(id, { desdeMapa: true }))
          .bindPopup(() => fichaPendiente(tarjeta()), GLOBO)
          .addTo(mapa);
        marcadores.set(id, marcador);
        entrar(marcador);
        continue;
      }
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon(cadMarcadores.caso(false, nuevos.has(id) ? "marcador-sos--pulso" : "")),
        title: `SOS de ${item.dataset.ciudadano}`,
        alt: `SOS de ${item.dataset.ciudadano}`,
        keyboard: true,
      })
        .on("click", () => elegirCaso(id, { desdeMapa: true }))
        .bindPopup(() => fichaPendiente(tarjeta()), GLOBO)
        .addTo(mapa);
      marcadores.set(id, marcador);
      entrar(marcador);
    }
    for (const [id, marcador] of marcadores) {
      if (vigentes.has(id)) continue;
      marcadores.delete(id);
      retirar(marcador);
    }
  }

  let sosSeleccionado = null;

  function seleccionar(id, { desdeMapa = false, conSereno = false } = {}) {
    sosSeleccionado = id;
    for (const item of document.querySelectorAll(".sos--seleccionado")) item.classList.remove("sos--seleccionado");
    for (const boton of document.querySelectorAll("[data-seleccionar-sos][aria-pressed=true]")) boton.setAttribute("aria-pressed", "false");
    const item = document.getElementById(`sos-${id}`);
    item?.querySelector("[data-seleccionar-sos]")?.setAttribute("aria-pressed", "true");
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
    if (!marcador) return;
    if (desdeMapa) {
      centrar(marcador);
      return;
    }
    // Se encuadran el caso y el sereno sugerido; si no hay ninguno, se vuela al caso con el zoom máximo.
    if (conSereno && lineaDespacho) encuadrar(lineaDespacho.getBounds(), marcador, claseLlegada(id));
    else volarA(marcador, { clase: claseLlegada(id) });
  }

  function deseleccionar() {
    sosSeleccionado = null;
    for (const item of document.querySelectorAll(".sos--seleccionado")) item.classList.remove("sos--seleccionado");
    for (const boton of document.querySelectorAll("[data-seleccionar-sos][aria-pressed=true]")) boton.setAttribute("aria-pressed", "false");
    resaltar(marcadores, null);
    mapa.closePopup();
    actualizarAsignacion();
    dibujarDespacho();
  }

  // ---------- Línea de despacho: del caso elegido al sereno disponible más cercano ----------
  // Ayuda a decidir; no asigna nada. La distancia es en línea recta.
  let lineaDespacho = null;
  const color = getComputedStyle(document.documentElement).getPropertyValue("--color-primario").trim() || "#1D4ED8";
  // El sereno sugerido para el caso elegido (el disponible más cercano), para el detalle del caso en el mapa.
  let sugerencia = null;
  function dibujarDespacho() {
    calcularDespacho();
    // Si el detalle del caso elegido está abierto, se rehace: su sereno sugerido puede haber cambiado (llega una posición
    // nueva, un sereno deja de estar disponible…). Leaflet vuelve a llamar a fichaPendiente al actualizarlo.
    const marcador = sosSeleccionado && marcadores.get(sosSeleccionado);
    if (marcador?.isPopupOpen()) marcador.getPopup().update();
  }

  function calcularDespacho() {
    lineaDespacho?.remove();
    lineaDespacho = null;
    sugerencia = null;
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
    const texto = distancia(cercano.metros);
    sugerencia = { nombre: cercano.s.dataset.nombre, texto };
    lineaDespacho = L.polyline([origen, cercano.destino], { color, weight: 3, dashArray: "8 7", interactive: false, className: "linea-despacho" })
      .bindTooltip(texto, { permanent: true, direction: "center", className: "etiqueta-despacho" })
      .addTo(mapa);
    cercano.s.classList.add("sereno--sugerido");
    cercano.s.querySelector(".sereno__cabecera")?.insertAdjacentHTML("afterend",
      `<p class="sereno__distancia" data-distancia>Más cercano al caso elegido · ${texto}</p>`);
  }

  // HU25-CA01/CA02/CA03: un marcador por caso en curso, distinto por origen (color) y por estado (con borde; reporte en
  // revisión con borde discontinuo). Al elegirlo se ven su identificador, origen y estado, con un enlace a su detalle.
  // Como los demás, el marcador de un caso que sigue con el mismo estado se conserva (solo cambia su detalle).
  function dibujarCasosEnCurso(vinculados) {
    const vigentes = new Set();
    for (const item of regionCasosMapa?.querySelectorAll("[data-caso-mapa]") ?? []) {
      const id = item.dataset.casoMapa;
      if (vinculados.get(id)?.enElLugar) continue; // lo dibuja el marcador conjunto con su sereno
      const reporte = item.dataset.origen === "Reporte";
      const revision = item.dataset.estado === "Recibido" || item.dataset.estado === "En revisión";
      const clase = `marcador-caso--en-curso${revision ? " marcador-caso--revision" : ""}`;
      const texto = `${item.dataset.origen} · ${item.dataset.estado} · ${item.dataset.ciudadano}`;
      const sereno = vinculados.get(id); // el sereno asignado, si tiene ubicación
      const detalle = ficha({
        tipo: reporte ? "reporte" : "sos", etiqueta: item.dataset.origen, estado: item.dataset.estado, titulo: item.dataset.ciudadano,
        datos: [["Sereno", sereno ? `${sereno.sereno.dataset.nombre} · a ${distancia(sereno.metros)}` : ""]],
        acciones: accion(item.dataset.detalle, "Abrir el caso", false),
      });
      vigentes.add(id);
      const existente = marcadoresEnCurso.get(id);
      if (existente?.forma === `${clase}|${texto}`) {
        deslizar(existente, [Number(item.dataset.lat), Number(item.dataset.lon)]);
        existente.setPopupContent(detalle);
        continue;
      }
      existente?.remove();
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon(cadMarcadores.caso(reporte, clase)),
        title: texto,
        alt: texto,
        keyboard: true,
      })
        .bindPopup(detalle, GLOBO)
        .on("click", (e) => centrar(e.target))
        .addTo(mapa);
      marcador.claves = [`caso:${id}`];
      marcador.forma = `${clase}|${texto}`;
      marcadoresEnCurso.set(id, marcador);
      if (!existente) entrar(marcador);
    }
    for (const [id, marcador] of marcadoresEnCurso) {
      if (vigentes.has(id)) continue;
      marcadoresEnCurso.delete(id);
      retirar(marcador);
    }
  }

  // 24/10/2026: elegir el caso es la acción principal, desde la cola o desde el mapa. El mapa lo encuadra con su sereno
  // sugerido (o vuela a él) y se abren los serenos para asignar; reemplaza a «Ver en el mapa» y «Elegir sereno».
  // 29/10/2026: ya no se abre el panel de serenos (tapaba el mapa): la barra de asignación rápida resuelve la elección.
  function elegirCaso(id, { desdeMapa = false } = {}) {
    seleccionar(id, { desdeMapa, conSereno: true });
  }

  // La tarjeta entera es el botón que elige el caso (su área cubre la tarjeta; ver central.css).
  function alHacerClicEnLista(evento) {
    const boton = evento.target.closest("[data-seleccionar-sos]");
    if (boton) elegirCaso(boton.closest("[data-sos]").dataset.sos);
  }
  region.addEventListener("click", alHacerClicEnLista);
  regionReportes?.addEventListener("click", alHacerClicEnLista);

  // ---------- HU19: serenos en el mismo mapa (sin rutas, líneas ni distancias) ----------
  const regionSerenos = document.querySelector("[data-region-serenos]");
  const marcadoresSerenos = new Map();

  // ---------- El sereno y su caso: en el lugar (un solo marcador) o en camino (una línea los une) ----------
  // A menos de esta distancia del punto del caso, el sereno está en el lugar: se dibujan juntos para que no parezcan dos
  // sitios distintos (antes cada marcador iba por su lado y, con unos metros de diferencia, quedaban uno al lado del otro).
  const EN_EL_LUGAR_METROS = 50;
  const lineasVinculo = [];

  // Caso en curso → su sereno asignado, con la distancia entre ambos (solo si los dos tienen ubicación).
  function vinculos() {
    const resultado = new Map();
    for (const sereno of regionSerenos.querySelectorAll("[data-sereno][data-caso]")) {
      if (!sereno.dataset.caso || !sereno.dataset.lat) continue;
      const caso = regionCasosMapa?.querySelector(`[data-caso-mapa="${CSS.escape(sereno.dataset.caso)}"]`);
      if (!caso) continue;
      const puntoCaso = L.latLng(Number(caso.dataset.lat), Number(caso.dataset.lon));
      const puntoSereno = L.latLng(Number(sereno.dataset.lat), Number(sereno.dataset.lon));
      const metros = puntoCaso.distanceTo(puntoSereno);
      resultado.set(sereno.dataset.caso, { caso, sereno, puntoCaso, puntoSereno, metros, enElLugar: metros <= EN_EL_LUGAR_METROS });
    }
    return resultado;
  }

  function dibujarEnCurso() {
    // Los marcadores se rehacen con cada aviso (la posición de un sereno llega cada pocos segundos): si el operador tenía
    // abierto el detalle de un caso o de un sereno, se vuelve a abrir en el marcador nuevo.
    const enMapa = () => [...marcadoresEnCurso.values(), ...marcadoresSerenos.values()];
    const abierto = enMapa().find((m) => m.isPopupOpen())?.claves ?? [];
    const vinculados = vinculos();
    dibujarCasosEnCurso(vinculados);
    dibujarSerenos(vinculados);
    if (abierto.length) enMapa().find((m) => m.claves?.some((c) => abierto.includes(c)))?.openPopup();
    dibujarRecorridos(vinculados);
  }

  // En camino: el recorrido por calles del sereno hasta su caso. El servidor lo busca con la misma red de calles y el mismo A*
  // que la app, así que la central ve la ruta que sigue el sereno. Se vuelve a pedir solo cuando un sereno con caso cambia de
  // posición; mientras llega la primera, o si el caso está fuera de la zona con calles cargadas, una línea recta punteada.
  const recorridos = new Map(); // sereno → { clave, caso, puntos }
  let pidiendoRecorridos = false;
  const claveRecorrido = (v) => `${v.caso.dataset.casoMapa}|${v.sereno.dataset.lat},${v.sereno.dataset.lon}`;

  function dibujarRecorridos(vinculados) {
    for (const linea of lineasVinculo.splice(0)) linea.remove();
    let faltan = false;
    for (const v of vinculados.values()) {
      if (v.enElLugar) continue;
      const origen = v.caso.dataset.origen === "Reporte" ? "reporte" : "sos";
      const clase = `linea-vinculo linea-vinculo--${origen}`;
      const recorrido = recorridos.get(v.sereno.dataset.sereno);
      if (recorrido?.clave !== claveRecorrido(v)) faltan = true;
      // Hasta que llega el nuevo, se mantiene el recorrido anterior del mismo caso (sin parpadeo cada 15 s).
      if (recorrido?.caso === v.caso.dataset.casoMapa && recorrido.puntos) {
        const trazo = { lineCap: "round", lineJoin: "round", interactive: false };
        lineasVinculo.push(
          L.polyline(recorrido.puntos, { ...trazo, color: "#FFFFFF", weight: 8, opacity: 0.9, className: clase }).addTo(mapa),
          L.polyline(recorrido.puntos, { ...trazo, color: "#0B1F44", weight: 4, opacity: 0.9, className: clase }).addTo(mapa));
      } else {
        lineasVinculo.push(L.polyline([v.puntoSereno, v.puntoCaso], { color: "#0B1F44", weight: 4, opacity: 0.85, dashArray: "1 9", lineCap: "round", interactive: false, className: clase }).addTo(mapa));
      }
    }
    if (faltan) pedirRecorridos(vinculados);
  }

  async function pedirRecorridos(vinculados) {
    if (pidiendoRecorridos) return; // al terminar se redibuja y, si algo cambió entretanto, se vuelve a pedir
    pidiendoRecorridos = true;
    const claves = new Map([...vinculados.values()].map((v) => [v.sereno.dataset.sereno, { clave: claveRecorrido(v), caso: v.caso.dataset.casoMapa }]));
    try {
      const respuesta = await fetch("/panel/central?handler=Rutas", { headers: { Accept: "application/json" }, credentials: "same-origin" });
      if (respuesta.redirected || !respuesta.ok) return;
      const rutas = new Map((await respuesta.json()).map((r) => [r.sereno, r]));
      // Un sereno sin recorrido en la respuesta queda con la línea recta (y no se pide otra vez hasta que se mueva).
      for (const [sereno, pedido] of claves) {
        const ruta = rutas.get(sereno);
        recorridos.set(sereno, { ...pedido, puntos: ruta?.caso === pedido.caso ? ruta.puntos : null });
      }
    } catch {
      return;
    } finally {
      pidiendoRecorridos = false;
    }
    dibujarRecorridos(vinculos());
  }

  // Un solo marcador: el del caso, con el escudo del sereno encima y la etiqueta «En el lugar».
  const fichaEnElLugar = (v) => ficha({
    tipo: v.caso.dataset.origen === "Reporte" ? "reporte" : "sos", etiqueta: v.caso.dataset.origen, estado: v.caso.dataset.estado, titulo: v.caso.dataset.ciudadano,
    datos: [["Sereno", `${v.sereno.dataset.nombre} · en el lugar`], ["Posición", `a ${Math.round(v.metros)} m del caso · ${v.sereno.dataset.hora}`]],
    acciones: accion(v.caso.dataset.detalle, "Abrir el caso", false),
  });

  function marcadorEnElLugar(v) {
    const reporte = v.caso.dataset.origen === "Reporte";
    const disponible = v.sereno.dataset.disponible === "true";
    const texto = `${v.caso.dataset.origen} · ${v.caso.dataset.estado} · sereno ${v.sereno.dataset.nombre} en el lugar`;
    return L.marker(v.puntoCaso, {
      icon: L.divIcon({
        className: `marcador-conjunto marcador-conjunto--${reporte ? "reporte" : "sos"}`,
        html: `<span class="conjunto__caso ${reporte ? "marcador-reporte" : "marcador-sos"} marcador-caso--en-curso">${reporte ? DOCUMENTO : SIRENA}</span>`
          + `<span class="conjunto__sereno marcador-sereno">${escudo(disponible)}</span><span class="conjunto__etiqueta">En el lugar</span>`,
        iconSize: [34, 34], iconAnchor: [17, 17], popupAnchor: [0, -24],
      }),
      title: texto,
      alt: texto,
      keyboard: true,
      zIndexOffset: 500,
    }).bindPopup(fichaEnElLugar(v), GLOBO);
  }

  const fichaSereno = (item, disponible) => ficha({
    tipo: "sereno", etiqueta: "Sereno", estado: disponible ? "Disponible" : "No disponible", titulo: item.dataset.nombre,
    datos: [["Ubicación", `Última a las ${item.dataset.hora}`]],
  });

  // Un sereno que sigue igual (mismo caso en el lugar, o misma disponibilidad) conserva su marcador y se desliza a su nueva
  // posición; si cambió de forma, el marcador se rehace.
  function dibujarSerenos(vinculados) {
    const enElLugar = new Map([...vinculados.values()].filter((v) => v.enElLugar).map((v) => [v.sereno.dataset.sereno, v]));
    const vigentes = new Set();
    for (const item of regionSerenos.querySelectorAll("[data-sereno][data-lat]")) {
      if (!item.dataset.lat) continue; // HU19-CA04: sin ubicación no hay marcador
      const id = item.dataset.sereno;
      const disponible = item.dataset.disponible === "true";
      const v = enElLugar.get(id);
      const forma = v ? `conjunto:${v.caso.dataset.casoMapa}:${disponible}` : `sereno:${disponible}`;
      const existente = marcadoresSerenos.get(id);
      vigentes.add(id);
      if (existente?.forma === forma) {
        deslizar(existente, v ? v.puntoCaso : [Number(item.dataset.lat), Number(item.dataset.lon)]);
        existente.setPopupContent(v ? fichaEnElLugar(v) : fichaSereno(item, disponible));
        continue;
      }
      existente?.remove();
      if (v) {
        const conjunto = marcadorEnElLugar(v).on("click", () => seleccionarSereno(id, { desdeMapa: true })).addTo(mapa);
        conjunto.claves = [`sereno:${id}`, `caso:${v.caso.dataset.casoMapa}`];
        conjunto.forma = forma;
        marcadoresSerenos.set(id, conjunto);
        continue;
      }
      const texto = `Sereno ${item.dataset.nombre}, ${disponible ? "disponible" : "no disponible"}`;
      const marcador = L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon(cadMarcadores.sereno(disponible)),
        title: texto,
        alt: texto,
        keyboard: true,
      })
        .bindPopup(fichaSereno(item, disponible), GLOBO)
        .on("click", () => seleccionarSereno(id, { desdeMapa: true }))
        .addTo(mapa);
      marcador.claves = [`sereno:${id}`];
      marcador.forma = forma;
      marcadoresSerenos.set(id, marcador);
      entrar(marcador);
    }
    for (const [id, marcador] of marcadoresSerenos) {
      if (vigentes.has(id)) continue;
      marcadoresSerenos.delete(id);
      retirar(marcador);
    }
  }

  // La tarjeta elegida y su botón (aria-pressed) van juntos; también se repone tras resincronizar la lista.
  function marcarSereno(id) {
    for (const item of regionSerenos.querySelectorAll(".sereno--seleccionado")) {
      item.classList.remove("sereno--seleccionado");
      item.querySelector("[data-ver-sereno]")?.setAttribute("aria-pressed", "false");
    }
    const item = id && document.getElementById(`sereno-${id}`);
    item?.classList.add("sereno--seleccionado");
    item?.querySelector("[data-ver-sereno]")?.setAttribute("aria-pressed", "true");
    return item || null;
  }

  function seleccionarSereno(id, { desdeMapa = false } = {}) {
    const item = marcarSereno(id);
    if (item) {
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
  });

  // ---------- HU20: asignar el SOS seleccionado a un sereno disponible ----------
  // El operador elige al sereno en la barra de asignación rápida (el más cercano o el que busca); aquí solo se pide
  // confirmación. Las tarjetas de «Serenos» ya no asignan: solo llevan el mapa al sereno.
  const confirmacion = document.querySelector("[data-confirmar-asignacion]");
  const textoConfirmacion = confirmacion.querySelector("[data-texto-confirmacion]");

  function actualizarAsignacion() {
    const sos = sosSeleccionado && document.getElementById(`sos-${sosSeleccionado}`);
    if (!sos) {
      sosSeleccionado = null;
      confirmacion.hidden = true;
    }
    actualizarBarraAsignacion();
  }

  // ---------- Asignación rápida (29/10/2026): el más cercano a un clic y un buscador por nombre ----------
  // Con el caso elegido, la barra ofrece al sereno disponible más cercano y un combobox para buscar a otro (flechas + Intro,
  // «/» para enfocarlo). Los candidatos salen de la lista de serenos, así que se rehace con cada resincronización.
  const barra = document.querySelector("[data-asignar-rapido]");
  const arCaso = barra.querySelector("[data-ar-caso]");
  const arVacio = barra.querySelector("[data-ar-vacio]");
  const arFila = barra.querySelector("[data-ar-fila]");
  const arSugerido = barra.querySelector("[data-ar-sugerido]");
  const arBuscar = barra.querySelector("[data-ar-buscar]");
  const arLista = barra.querySelector("[data-ar-lista]");
  const arAnuncio = barra.querySelector("[data-ar-anuncio]");
  let arCasoActual = null;
  let arCandidatos = [];
  let arVisibles = [];
  let arActivo = null; // id del sereno resaltado en la lista (se conserva al rehacerla)
  let arCerradaPara = null; // el operador cerró la barra de este caso: vuelve al elegir otro

  const sinTildes = (texto) => texto.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();

  function actualizarBarraAsignacion() {
    const sos = sosSeleccionado && document.getElementById(`sos-${sosSeleccionado}`);
    if (!sos) arCerradaPara = null;
    barra.hidden = !sos || arCerradaPara === sosSeleccionado;
    if (barra.hidden) {
      arCasoActual = null;
      cerrarListaAsignacion();
      return;
    }
    if (arCasoActual !== sosSeleccionado) {
      arCasoActual = sosSeleccionado;
      arBuscar.value = "";
      arActivo = null;
    }
    arCaso.textContent = `Asignar el ${sos.dataset.origen === "Reporte" ? "reporte" : "SOS"} de ${sos.dataset.ciudadano}`;
    const origen = sos.dataset.lat ? L.latLng(Number(sos.dataset.lat), Number(sos.dataset.lon)) : null;
    arCandidatos = [...regionSerenos.querySelectorAll('[data-sereno][data-disponible="true"]')]
      .map((item) => ({
        item, id: item.dataset.sereno, nombre: item.dataset.nombre, buscable: sinTildes(item.dataset.nombre),
        metros: origen && item.dataset.lat ? origen.distanceTo(L.latLng(Number(item.dataset.lat), Number(item.dataset.lon))) : null,
      }))
      .sort((a, b) => (a.metros ?? Infinity) - (b.metros ?? Infinity) || a.nombre.localeCompare(b.nombre, "es"));
    arVacio.hidden = arCandidatos.length > 0;
    arFila.hidden = arCandidatos.length === 0;
    const cercano = arCandidatos.find((c) => c.metros !== null);
    arSugerido.hidden = !cercano;
    if (cercano) {
      arSugerido.querySelector("[data-ar-sugerido-nombre]").textContent = `Asignar a ${cercano.nombre}`;
      arSugerido.querySelector("[data-ar-sugerido-meta]").textContent = `Más cercano · ${distancia(cercano.metros)}`;
      arSugerido.dataset.sereno = cercano.id;
      arSugerido.setAttribute("aria-label", `Asignar a ${cercano.nombre}, el más cercano, a ${distancia(cercano.metros)}`);
    }
    pintarListaAsignacion();
  }

  function pintarListaAsignacion() {
    const consulta = sinTildes(arBuscar.value.trim());
    arVisibles = arCandidatos.filter((c) => c.buscable.includes(consulta));
    if (!arVisibles.some((c) => c.id === arActivo)) arActivo = arVisibles[0]?.id ?? null;
    arLista.innerHTML = arVisibles.length
      ? arVisibles.map((c) => `<li role="option" id="ar-op-${c.id}" data-id="${c.id}" aria-selected="${c.id === arActivo}">`
        + `<span class="asignar-rapido__nombre">${escapar(c.nombre)}</span>`
        + `<span class="asignar-rapido__distancia">${c.metros === null ? "sin ubicación" : distancia(c.metros)}</span></li>`).join("")
      : `<li class="asignar-rapido__nada" role="presentation">Ningún sereno disponible se llama así.</li>`;
    arBuscar.setAttribute("aria-activedescendant", arActivo ? `ar-op-${arActivo}` : "");
    arLista.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: "nearest" });
    arAnuncio.textContent = arLista.hidden ? "" : `${arVisibles.length} ${arVisibles.length === 1 ? "sereno" : "serenos"}`;
  }

  function abrirListaAsignacion() {
    arLista.hidden = false;
    arBuscar.setAttribute("aria-expanded", "true");
    pintarListaAsignacion();
  }

  function cerrarListaAsignacion() {
    arLista.hidden = true;
    arBuscar.setAttribute("aria-expanded", "false");
    arBuscar.setAttribute("aria-activedescendant", "");
  }

  function asignarDesdeBarra(id) {
    const item = id && regionSerenos.querySelector(`[data-sereno="${CSS.escape(id)}"]`);
    if (!item) return;
    cerrarListaAsignacion();
    abrirConfirmacion(item);
  }

  function moverActivo(paso) {
    if (!arVisibles.length) return;
    const actual = arVisibles.findIndex((c) => c.id === arActivo);
    arActivo = arVisibles[(actual + paso + arVisibles.length) % arVisibles.length].id;
    pintarListaAsignacion();
  }

  function cerrarBarraAsignacion() {
    arCerradaPara = sosSeleccionado;
    actualizarBarraAsignacion();
  }

  barra.querySelector("[data-ar-cerrar]").addEventListener("click", cerrarBarraAsignacion);
  // Esc con la lista cerrada (la lista lo atiende primero) cierra la barra y deja el mapa libre.
  barra.addEventListener("keydown", (evento) => {
    if (evento.key === "Escape" && arLista.hidden && !evento.defaultPrevented) cerrarBarraAsignacion();
  });
  arSugerido.addEventListener("click", () => asignarDesdeBarra(arSugerido.dataset.sereno));
  arBuscar.addEventListener("focus", abrirListaAsignacion);
  arBuscar.addEventListener("input", abrirListaAsignacion);
  arBuscar.addEventListener("blur", cerrarListaAsignacion);
  arBuscar.addEventListener("keydown", (evento) => {
    if (evento.key === "ArrowDown" || evento.key === "ArrowUp") {
      evento.preventDefault();
      if (arLista.hidden) abrirListaAsignacion();
      else moverActivo(evento.key === "ArrowDown" ? 1 : -1);
    } else if (evento.key === "Enter") {
      evento.preventDefault();
      asignarDesdeBarra(arActivo);
    } else if (evento.key === "Escape" && !arLista.hidden) {
      evento.preventDefault();
      cerrarListaAsignacion();
    }
  });
  // El clic en una opción no debe quitarle el foco al campo antes de contar (blur cerraría la lista primero).
  arLista.addEventListener("mousedown", (evento) => evento.preventDefault());
  arLista.addEventListener("click", (evento) => asignarDesdeBarra(evento.target.closest("[data-id]")?.dataset.id));
  document.addEventListener("keydown", (evento) => {
    if (evento.key !== "/" || evento.ctrlKey || evento.metaKey || evento.altKey || barra.hidden || arFila.hidden) return;
    if (evento.target.closest("input, textarea, select, [contenteditable]")) return;
    evento.preventDefault();
    arBuscar.focus();
  });

  // Filtro por nombre del panel de serenos: oculta tarjetas, no toca la lista que el servidor resincroniza.
  const filtroSerenos = document.querySelector("[data-filtro-serenos]");
  const sinCoincidencias = document.querySelector("[data-sin-coincidencias]");
  function aplicarFiltroSerenos() {
    const consulta = sinTildes(filtroSerenos.value.trim());
    const items = [...regionSerenos.querySelectorAll("[data-sereno]")];
    let visibles = 0;
    for (const item of items) {
      const coincide = sinTildes(item.dataset.nombre).includes(consulta);
      item.classList.toggle("sereno--oculto", !coincide);
      if (coincide) visibles++;
    }
    sinCoincidencias.hidden = !consulta || visibles > 0 || items.length === 0;
  }
  filtroSerenos.addEventListener("input", aplicarFiltroSerenos);

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
  // La leyenda y el filtro de la cola mandan sobre la misma capa: el chip siempre dice lo que se ve.
  function fijarCapa(capa, visible) {
    document.querySelector(`[data-capa="${capa}"]`)?.setAttribute("aria-pressed", String(visible));
    central.classList.toggle(`central--sin-${capa}`, !visible);
    if (capa === "sereno") lineaDespacho?.getElement()?.classList.toggle("visualmente-oculto", !visible);
  }
  for (const boton of document.querySelectorAll("[data-capa]")) {
    boton.addEventListener("click", () => fijarCapa(boton.dataset.capa, boton.getAttribute("aria-pressed") !== "true"));
  }

  // HU19-CA03: se vuelve a pedir el fragmento al servidor (fuente de verdad); se conserva la selección.
  async function refrescarSerenos() {
    const respuesta = await fetch("/panel/central?handler=Serenos", { headers: { Accept: "text/html" }, credentials: "same-origin" });
    if (respuesta.redirected || !respuesta.ok) return false;
    const seleccionado = regionSerenos.querySelector(".sereno--seleccionado")?.dataset.sereno;
    // Lo que solo sabe el navegador (elegido, sugerido, «Asignar» a la vista) se repone antes de medir: así la tarjeta no
    // cambia de alto por un instante ni parece moverse.
    cadMovimiento.actualizarRegion(regionSerenos, await respuesta.text(), {
      animar: true,
      despues: () => {
        if (seleccionado) marcarSereno(seleccionado);
        aplicarFiltroSerenos();
        actualizarAsignacion();
        calcularDespacho();
      },
    });
    dibujarEnCurso();
    if (seleccionado) resaltar(marcadoresSerenos, seleccionado);
    dibujarDespacho();
    actualizarIndicadores();
    return true;
  }

  // ---------- Indicadores de la barra y filtro de la cola (rediseño CAD v2) ----------
  // Lo que cambia se nota: la cifra late una vez al cambiar (solo escala, sin movimiento si se pide reducirlo).
  function fijarIndicador(nombre, valor, etiqueta) {
    const indicador = document.querySelector(`[data-indicador="${nombre}"]`);
    const cifra = indicador?.querySelector("[data-valor]");
    if (!cifra) return;
    if (etiqueta) indicador.querySelector("[data-etiqueta]").textContent = etiqueta;
    cadMovimiento.fijarCifra(cifra, valor);
  }

  function actualizarIndicadores() {
    const sos = Number(contador.textContent) || 0;
    const reportes = Number(contadorReportes?.textContent) || 0;
    const libres = regionSerenos.querySelectorAll('[data-sereno][data-disponible="true"]').length;
    fijarIndicador("sos", sos);
    document.querySelector('[data-indicador="sos"]')?.classList.toggle("indicador--activo", sos > 0);
    fijarIndicador("por-asignar", sos + reportes);
    fijarIndicador("serenos-libres", libres, libres === 1 ? "sereno libre" : "serenos libres");
    for (const [clave, valor] of [["sos", sos], ["reportes", reportes]]) {
      cadMovimiento.fijarCifra(document.querySelector(`[data-filtro-total="${clave}"]`), valor);
    }
  }

  const cola = document.getElementById("panel-cola");
  for (const boton of document.querySelectorAll("[data-filtro-cola]")) {
    boton.addEventListener("click", () => {
      for (const otro of document.querySelectorAll("[data-filtro-cola]")) otro.setAttribute("aria-pressed", String(otro === boton));
      const filtro = boton.dataset.filtroCola;
      cola.classList.toggle("cola--solo-sos", filtro === "sos");
      cola.classList.toggle("cola--solo-reportes", filtro === "reportes");
      // 29/10/2026: «SOS» deja solo los SOS en el mapa y «Reportes» solo los reportes (con las líneas de sus serenos);
      // «Todo» vuelve a mostrar ambos. Con muchos reportes, el mapa no se llena de lo que no se está trabajando.
      fijarCapa("sos", filtro !== "reportes");
      fijarCapa("reporte", filtro !== "sos");
      // El caso elegido que queda fuera del filtro se suelta: ni su tarjeta ni su marcador están a la vista.
      const elegido = sosSeleccionado && document.getElementById(`sos-${sosSeleccionado}`);
      const esReporte = elegido?.dataset.origen === "Reporte";
      if (elegido && ((filtro === "sos" && esReporte) || (filtro === "reportes" && !esReporte))) deseleccionar();
    });
  }

  // ---------- Aviso visual y sonoro ----------
  // Los sonidos y su botón (barra superior) están en sonidos.js (24/10/2026): activados por defecto.
  let audio = null;

  // Tono sintetizado solo para la cancelación de un SOS, que no tiene sonido propio.
  function tono(frecuencias, duracion) {
    if (!CadSonidos.activos) return false;
    audio ??= new AudioContext();
    if (audio.state !== "running") audio.resume();
    let inicio = audio.currentTime;
    for (const frecuencia of frecuencias) {
      const oscilador = audio.createOscillator();
      const volumen = audio.createGain();
      oscilador.type = "sine";
      oscilador.frequency.value = frecuencia;
      volumen.gain.setValueAtTime(0.2, inicio);
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
    if (CadSonidos.reproducir("sos-nuevo"))
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

  // Un reporte que entra a «Reportes admitidos» (otro operador lo admitió o lo registró) suena una vez.
  const reportesVistos = new Set([...(regionReportes?.querySelectorAll("[data-sos]") ?? [])].map((el) => el.dataset.sos));
  function avisarReportesNuevos() {
    let hayNuevo = false;
    for (const el of regionReportes.querySelectorAll("[data-sos]")) {
      if (reportesVistos.has(el.dataset.sos)) continue;
      reportesVistos.add(el.dataset.sos);
      hayNuevo = true;
    }
    if (hayNuevo) CadSonidos.reproducir("reporte-admitido");
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
    // Fase 1 de movimiento (26/10/2026): las listas se actualizan en su sitio (actualizarRegion, movimiento.js): lo nuevo entra,
    // lo que se asignó se desvanece, el resto se desliza, y la tarjeta bajo el cursor no se mueve. La selección y la marca
    // «Nuevo» se reponen antes de medir.
    const reponerSeleccion = () => {
      for (const id of nuevos) document.getElementById(`sos-${id}`)?.classList.add("sos--nuevo");
      const elegido = sosSeleccionado && document.getElementById(`sos-${sosSeleccionado}`);
      elegido?.classList.add("sos--seleccionado");
      elegido?.querySelector("[data-seleccionar-sos]")?.setAttribute("aria-pressed", "true");
    };
    cadMovimiento.actualizarRegion(region, await respuesta.text(), { animar: true, despues: reponerSeleccion });
    cadMovimiento.fijarCifra(contador, region.querySelector("[data-lista-sos]").dataset.total);
    if (regionReportes) {
      const reportes = await fetch("/panel/central?handler=Reportes", { headers: { Accept: "text/html" }, credentials: "same-origin" });
      if (reportes.ok && !reportes.redirected) {
        const { nuevos: llegados } = cadMovimiento.actualizarRegion(regionReportes, await reportes.text(), { animar: true, despues: reponerSeleccion });
        // Un reporte que llega mientras la central está abierta se marca un momento (un anillo que se apaga solo).
        for (const id of llegados) {
          const tarjeta = document.getElementById(`sos-${id}`);
          tarjeta?.classList.add("sos--llegada");
          tarjeta?.addEventListener("animationend", () => tarjeta.classList.remove("sos--llegada"), { once: true });
        }
        cadMovimiento.fijarCifra(contadorReportes, regionReportes.querySelector("[data-lista-reportes]").dataset.total);
        avisarReportesNuevos();
      }
    }
    if (regionCasosMapa) {
      const enCurso = await fetch("/panel/central?handler=CasosMapa", { headers: { Accept: "text/html" }, credentials: "same-origin" });
      if (enCurso.ok && !enCurso.redirected) cadMovimiento.actualizarRegion(regionCasosMapa, await enCurso.text());
    }
    dibujarMarcadores();
    dibujarEnCurso();
    if (sosSeleccionado) resaltar(marcadores, sosSeleccionado);
    actualizarAsignacion();
    actualizarIndicadores();
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
    // Acuse cuando el SOS ya se muestra y se ha avisado: el servidor anota su primera presentación (analítica) y, con la
    // medición activada, la de RNF02.
    if (document.getElementById(`sos-${id}`))
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

  // Detalle de un caso o de un sereno en el mapa (24/10/2026): sin el identificador interno (largo y sin uso para el
  // operador); con quién es, lo que sirve para decidir y un acceso a la siguiente acción.
  const GLOBO = { className: "ficha-globo", minWidth: 240, maxWidth: 300 };

  function ficha({ tipo, etiqueta, estado, hora, titulo, datos = [], acciones = "" }) {
    const filas = datos.filter(([, valor]) => valor).map(([nombre, valor]) => `<div><dt>${escapar(nombre)}</dt><dd>${escapar(valor)}</dd></div>`).join("");
    return `<div class="ficha ficha--${tipo}">`
      + `<p class="ficha__cabecera"><span class="ficha__origen">${escapar(etiqueta)}</span>`
      + (estado ? `<span class="ficha__estado">${escapar(estado)}</span>` : "")
      + (hora ? `<span class="ficha__hora">${escapar(hora)}</span>` : "") + `</p>`
      + `<p class="ficha__titulo">${escapar(titulo)}</p>`
      + (filas ? `<dl class="ficha__datos">${filas}</dl>` : "")
      + (acciones ? `<div class="ficha__acciones">${acciones}</div>` : "")
      + `</div>`;
  }

  function distancia(metros) {
    return metros < 1000 ? `${Math.round(metros)} m` : `${(metros / 1000).toFixed(1).replace(".", ",")} km`;
  }

  function accion(href, texto, principal) {
    return `<a class="boton ${principal ? "boton--principal" : "boton--secundario"} boton--compacto" href="${escapar(href)}">${escapar(texto)}</a>`;
  }

  // Detalle de un caso pendiente: informativo, con el sereno sugerido si el caso está elegido. La acción es elegirlo (ya
  // hecho al tocarlo) y asignar desde «Serenos»; aquí solo se puede abrir su detalle.
  function fichaPendiente(item) {
    const reporte = item.dataset.origen === "Reporte";
    const sugerido = sosSeleccionado === item.dataset.sos ? sugerencia : null;
    return ficha({
      tipo: reporte ? "reporte" : "sos", etiqueta: reporte ? "Reporte" : "SOS", estado: "Pendiente",
      hora: `${reporte ? "Admitido" : "Recibido"} ${item.dataset.hora}`, titulo: item.dataset.ciudadano,
      datos: [
        ["Tipo", reporte ? item.dataset.categoria : item.dataset.clase],
        ["Ubicación", reporte ? "" : `Teléfono del ciudadano · ± ${item.dataset.precision} m`],
        ["Sugerido", sugerido ? `${sugerido.nombre} · a ${sugerido.texto}` : "Ningún sereno disponible con ubicación"],
      ],
      acciones: accion(item.dataset.detalle, reporte ? "Abrir el reporte" : "Abrir el SOS", false),
    });
  }

  function escapar(texto) {
    return String(texto ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
  }

  dibujarMarcadores();
  dibujarEnCurso();
  mapaDibujado = true;
  iniciar();

  // Acceso para la verificación automatizada (estado de la conexión y vista del mapa, sin datos).
  window.cadCentral = { conexion, refrescar, refrescarSerenos, mapa };
})();
