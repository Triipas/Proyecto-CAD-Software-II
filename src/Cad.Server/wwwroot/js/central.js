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
  // Casos con sereno (asignados, en camino o en atención): siguen en la cola tras los pendientes. Al elegir uno se dibuja,
  // solo entonces, el recorrido de su sereno (29/10/2026).
  const regionAsignados = document.querySelector("[data-region-asignados]");
  const contadorAsignados = document.querySelector("[data-contador-asignados]");
  // Bandeja (05/11/2026): los reportes recibidos y en revisión, con su propio fragmento (sin marcador propio: ya están entre los
  // casos en curso del mapa).
  const regionRevisar = document.querySelector("[data-region-revisar]");
  const contadorRevisar = document.querySelector("[data-contador-revisar]");
  let casoConRuta = null;
  let pestana = "sos"; // pestaña de la bandeja que se mira (ver «Pestañas de la bandeja»)
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
  const ZOOM_LLEGADA = 17; // al enfocar un caso recién llegado: se ve el caso y su entorno, no solo la calle
  const DURACION_VUELO = 0.9; // segundos (Leaflet)

  const panelAbierto = () => document.querySelector(".central")?.classList.contains("central--con-panel") ?? false;

  // Franja horizontal del mapa que no tapan los paneles flotantes abiertos (en píxeles del contenedor).
  function franjaLibre() {
    const ancho = contenedorMapa.clientWidth;
    let izquierda = 0;
    let derecha = ancho;
    for (const panel of document.querySelectorAll("[data-panel]:not([hidden])")) {
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
    // Con el panel del caso abierto, el globo repetiría lo que el panel ya muestra.
    const llegar = () => { if (!panelAbierto()) marcador.openPopup(); marcarLlegada(punto, clase); };
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
    const llegar = () => { if (!panelAbierto()) marcador?.openPopup(); if (marcador) marcarLlegada(marcador.getLatLng(), clase); };
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

  // Último caso llegado (SOS o reporte, 03/11/2026): el mapa se enfoca en él y un sonar, como el de la vigilancia, late a su
  // alrededor. Solo hay uno: el siguiente que llega lo releva. Se apaga al abrirlo el operador o al salir de la lista.
  // 04/11/2026: el reporte por revisar (Recibido o En revisión, enviado desde la app) no está en la cola sino entre los casos
  // en curso del mapa (marcadoresEnCurso); antes el sonar solo lo buscaba en la cola y ese reporte llegaba sin enfoque.
  let ultimoLlegado = null; // { id, capa, marcador }
  function apagarUltimo() {
    if (!ultimoLlegado) return;
    ultimoLlegado.marcador.getElement()?.classList.remove("marcador--ultimo");
    ultimoLlegado.capa.remove();
    ultimoLlegado = null;
  }

  // El marcador del caso (en la cola o en curso) se rehizo o se movió: el sonar lo sigue.
  function seguirUltimo(id, marcador) {
    if (ultimoLlegado?.id !== id) return;
    ultimoLlegado.marcador = marcador;
    ultimoLlegado.capa.setLatLng(marcador.getLatLng());
    marcador.getElement()?.classList.add("marcador--ultimo");
  }

  function destacarUltimo(id, { reporte = claseLlegada(id) !== "" } = {}) {
    const marcador = marcadores.get(id) ?? marcadoresEnCurso.get(id);
    if (!marcador) return;
    apagarUltimo();
    const punto = marcador.getLatLng();
    const capa = L.marker(punto, {
      icon: L.divIcon({ className: `sonar-llegada${reporte ? " sonar-llegada--reporte" : ""}`, html: "<span></span><span></span>", iconSize: [160, 160], iconAnchor: [80, 80] }),
      interactive: false,
      keyboard: false,
      zIndexOffset: -1000,
    }).addTo(mapa);
    marcador.getElement()?.classList.add("marcador--ultimo");
    ultimoLlegado = { id, capa, marcador };
    traerMapaALaVista();
    const destino = centroLibre(punto, ZOOM_LLEGADA);
    if (reducido.matches) mapa.setView(destino, ZOOM_LLEGADA, { animate: false });
    else mapa.flyTo(destino, ZOOM_LLEGADA, { duration: DURACION_VUELO, easeLinearity: 0.2 });
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
        seguirUltimo(id, existente);
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
      if (ultimoLlegado?.marcador === marcador) apagarUltimo();
      retirar(marcador);
    }
  }

  let sosSeleccionado = null;

  function seleccionar(id, { desdeMapa = false, conSereno = false } = {}) {
    irAPestanaDe(id);
    seguirEnPanel(id);
    soltarAsignado();
    sosSeleccionado = id;
    for (const item of document.querySelectorAll(".sos--seleccionado")) item.classList.remove("sos--seleccionado");
    for (const boton of document.querySelectorAll("[data-seleccionar-sos][aria-pressed=true]")) boton.setAttribute("aria-pressed", "false");
    const item = document.getElementById(`sos-${id}`);
    item?.querySelector("[data-seleccionar-sos]")?.setAttribute("aria-pressed", "true");
    if (nuevos.delete(id)) marcadores.get(id)?.getElement()?.classList.remove("marcador-sos--pulso");
    if (ultimoLlegado?.id === id) apagarUltimo();
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

  function deseleccionar({ cerrarGlobo = true } = {}) {
    sosSeleccionado = null;
    for (const item of document.querySelectorAll(".sos--seleccionado")) item.classList.remove("sos--seleccionado");
    for (const boton of document.querySelectorAll("[data-seleccionar-sos][aria-pressed=true]")) boton.setAttribute("aria-pressed", "false");
    resaltar(marcadores, null);
    if (cerrarGlobo) mapa.closePopup();
    actualizarAsignacion();
    dibujarDespacho();
  }

  // ---------- Casos con sereno en la cola ----------
  // Elegirlo lleva el mapa al caso con su sereno y dibuja el recorrido de ese sereno; ningún otro recorrido se dibuja.
  // Casos con sereno y reportes por revisar: los dos se eligen con su marcador de casos en curso.
  function marcarAsignado(id) {
    for (const item of document.querySelectorAll("[data-asignado], [data-revisar]")) {
      const elegido = (item.dataset.asignado ?? item.dataset.revisar) === id;
      item.classList.toggle("sos--seleccionado", elegido);
      item.querySelector("[data-seleccionar-asignado], [data-seleccionar-revisar]")?.setAttribute("aria-pressed", String(elegido));
    }
  }

  function seleccionarAsignado(id, { desdeMapa = false } = {}) {
    irAPestanaDe(id);
    seguirEnPanel(id);
    if (sosSeleccionado) deseleccionar();
    casoConRuta = id;
    if (ultimoLlegado?.id === id) apagarUltimo();
    marcarAsignado(id);
    const item = document.getElementById(`asignado-${id}`) ?? document.getElementById(`revisar-${id}`);
    if (item && desdeMapa) {
      item.scrollIntoView({ block: "nearest" });
      item.focus();
    }
    resaltar(marcadoresEnCurso, id);
    dibujarEnCurso();
    const marcador = marcadoresEnCurso.get(id) ?? [...marcadoresSerenos.values()].find((m) => m.claves?.includes(`caso:${id}`));
    if (!marcador) return;
    if (desdeMapa) {
      centrar(marcador);
      return;
    }
    const v = vinculos().get(id);
    const clase = item?.dataset.origen === "Reporte" ? "llegada--reporte" : "";
    if (v && !v.enElLugar) encuadrar(L.latLngBounds([v.puntoCaso, v.puntoSereno]), marcador, clase);
    else volarA(marcador, { zoom: 17, clase });
  }

  function soltarAsignado() {
    if (!casoConRuta) return;
    casoConRuta = null;
    marcarAsignado(null);
    resaltar(marcadoresEnCurso, null);
    dibujarRecorridos(vinculos());
  }

  function contarAsignados() {
    if (!contadorAsignados) return;
    cadMovimiento.fijarCifra(contadorAsignados, document.querySelectorAll("[data-asignado]").length);
  }

  // El reporte por revisar se elige como un caso en curso (su marcador discontinuo); sin punto en el mapa, se abre en el panel.
  regionRevisar?.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-seleccionar-revisar]");
    if (!boton) return;
    const id = boton.closest("[data-revisar]").dataset.revisar;
    if (marcadoresEnCurso.has(id)) seleccionarAsignado(id);
    else abrirPanel(`/panel/reportes/${id}`);
  });

  regionAsignados?.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-seleccionar-asignado]");
    if (boton) seleccionarAsignado(boton.closest("[data-asignado]").dataset.asignado);
  });

  // ---------- Un clic en un hueco del mapa suelta lo elegido (04/11/2026) ----------
  // Igual que soltarlo desde la cola: el caso (SOS o reporte, en la cola o en curso) pierde su resaltado, su línea de despacho
  // o su recorrido, y el sereno elegido también se suelta. Leaflet no emite el «click» del mapa al tocar un marcador (no
  // burbujea), un globo o un control, ni al terminar un arrastre; el doble clic que acerca cancela el clic que lo precede.
  let soltarPendiente = null;
  function soltarTodo() {
    soltarPendiente = null;
    if (sosSeleccionado) deseleccionar();
    soltarAsignado();
    marcarSereno(null);
    resaltar(marcadoresSerenos, null);
  }
  mapa.on("click", () => {
    clearTimeout(soltarPendiente);
    soltarPendiente = setTimeout(soltarTodo, 250);
  });
  mapa.on("dblclick", () => clearTimeout(soltarPendiente));

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

  // Panel de serenos (06/11/2026): con un caso elegido, los disponibles muestran su distancia a él y se ordenan por cercanía; el
  // más cercano lleva el tinte y dice por qué es el primero. Sin caso elegido vuelven al orden del servidor (por nombre). No se
  // inserta ningún nodo en el fragmento del servidor: los textos van en elementos que él ya trae y se reponen tras resincronizar.
  const contextoSerenos = document.querySelector("[data-serenos-contexto]");
  let ordenPendiente = false;

  function fijarTexto(elemento, texto) {
    if (elemento && elemento.textContent !== texto) elemento.textContent = texto;
  }

  // Ordena las filas de una lista sin que se muevan bajo el cursor: si está sobre la lista, el orden espera a que salga.
  function ordenarFilas(lista, filas, animar) {
    if (!lista || filas.every((fila, i) => fila === lista.children[i])) return;
    if (regionSerenos.matches(":hover")) {
      ordenPendiente = true;
      return;
    }
    const animando = animar && !panelSerenos.hidden && cadMovimiento.duracion("base") > 0;
    const antes = animando ? new Map(filas.map((fila) => [fila, fila.getBoundingClientRect().top])) : null;
    const enfocado = lista.contains(document.activeElement) ? document.activeElement : null;
    for (const fila of filas) lista.appendChild(fila);
    if (enfocado && document.activeElement !== enfocado) enfocado.focus({ preventScroll: true });
    if (!antes) return;
    for (const fila of filas) {
      const dy = antes.get(fila) - fila.getBoundingClientRect().top;
      if (Math.abs(dy) > 0.5)
        fila.animate([{ transform: `translateY(${dy}px)` }, { transform: "none" }], { duration: cadMovimiento.duracion("base"), easing: cadMovimiento.curva("movimiento") });
    }
  }

  // Devuelve a los disponibles el orden que tenían a la vista (los que llegan nuevos, al final).
  function conservarOrden(ids) {
    const lista = regionSerenos.querySelector('[data-grupo-serenos="disponibles"] .lista-serenos');
    if (!lista) return;
    const puesto = (fila) => { const i = ids.indexOf(fila.dataset.sereno); return i < 0 ? ids.length : i; };
    const enfocado = lista.contains(document.activeElement) ? document.activeElement : null;
    for (const fila of [...lista.children].sort((a, b) => puesto(a) - puesto(b))) lista.appendChild(fila);
    if (enfocado && document.activeElement !== enfocado) enfocado.focus({ preventScroll: true });
  }

  function calcularDespacho({ animar = true } = {}) {
    lineaDespacho?.remove();
    lineaDespacho = null;
    sugerencia = null;
    for (const el of regionSerenos.querySelectorAll(".sereno--sugerido")) el.classList.remove("sereno--sugerido");
    const libres = [...regionSerenos.querySelectorAll('[data-sereno][data-disponible="true"]')];
    const caso = sosSeleccionado && document.getElementById(`sos-${sosSeleccionado}`);
    const origen = caso?.dataset.lat ? L.latLng(Number(caso.dataset.lat), Number(caso.dataset.lon)) : null;
    const medidas = new Map();
    let cercano = null;
    if (origen) {
      for (const s of libres) {
        if (!s.dataset.lat) continue;
        const destino = L.latLng(Number(s.dataset.lat), Number(s.dataset.lon));
        const metros = origen.distanceTo(destino);
        medidas.set(s, metros);
        if (!cercano || metros < cercano.metros) cercano = { s, destino, metros };
      }
    }
    const tipo = caso?.dataset.origen === "Reporte" ? "reporte" : "SOS";
    for (const s of libres) {
      fijarTexto(s.querySelector("[data-sereno-metros]"), medidas.has(s) ? distancia(medidas.get(s)) : "");
      const motivo = s.querySelector("[data-distancia]");
      if (!motivo) continue;
      const esElMasCercano = cercano?.s === s;
      motivo.hidden = !esElMasCercano;
      fijarTexto(motivo, esElMasCercano ? `Más cercano al ${tipo} elegido` : "");
    }
    // Por cercanía (los que no tienen ubicación, al final) o, sin caso elegido, en el orden del servidor.
    const lejos = Number.POSITIVE_INFINITY;
    const filas = [...libres].sort((a, b) => (medidas.get(a) ?? lejos) - (medidas.get(b) ?? lejos) || Number(a.dataset.orden) - Number(b.dataset.orden));
    ordenarFilas(regionSerenos.querySelector('[data-grupo-serenos="disponibles"] .lista-serenos'), filas, animar);
    const columna = regionSerenos.querySelector('[data-columna="disponibles"]');
    if (columna) columna.hidden = medidas.size === 0;
    if (contextoSerenos) {
      contextoSerenos.hidden = medidas.size === 0;
      if (medidas.size) fijarTexto(contextoSerenos.querySelector("[data-texto]"), `Ordenados por cercanía al ${tipo} ${/^registrado por/i.test(caso.dataset.ciudadano) ? caso.dataset.ciudadano.toLowerCase() : `de ${caso.dataset.ciudadano}`}`);
    }
    if (!cercano) return;
    const texto = distancia(cercano.metros);
    sugerencia = { nombre: cercano.s.dataset.nombre, texto };
    lineaDespacho = L.polyline([origen, cercano.destino], { color, weight: 3, dashArray: "8 7", interactive: false, className: "linea-despacho" })
      .bindTooltip(texto, { permanent: true, direction: "center", className: "etiqueta-despacho" })
      .addTo(mapa);
    cercano.s.classList.add("sereno--sugerido");
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
        seguirUltimo(id, existente);
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
        .on("click", () => seleccionarAsignado(id, { desdeMapa: true }))
        .addTo(mapa);
      marcador.claves = [`caso:${id}`];
      marcador.forma = `${clase}|${texto}`;
      marcadoresEnCurso.set(id, marcador);
      seguirUltimo(id, marcador);
      if (!existente) entrar(marcador);
    }
    for (const [id, marcador] of marcadoresEnCurso) {
      if (vigentes.has(id)) continue;
      marcadoresEnCurso.delete(id);
      if (ultimoLlegado?.marcador === marcador) apagarUltimo();
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
    rotularConCaso(vinculados);
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
      if (v.enElLugar || v.caso.dataset.casoMapa !== casoConRuta) continue; // solo el recorrido del caso elegido
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
    const claves = new Map([...vinculados.values()].filter((v) => v.caso.dataset.casoMapa === casoConRuta)
      .map((v) => [v.sereno.dataset.sereno, { clave: claveRecorrido(v), caso: v.caso.dataset.casoMapa }]));
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
        const conjunto = marcadorEnElLugar(v).on("click", () => {
          seleccionarSereno(id, { desdeMapa: true });
          seleccionarAsignado(v.caso.dataset.casoMapa, { desdeMapa: true });
        }).addTo(mapa);
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
    if (desdeMapa) mostrarSerenos(true);
    const item = marcarSereno(id);
    // 06/11/2026: la pestaña de la bandeja pasa a la del caso que atiende el sereno (un SOS en curso, «En curso»), para ver a la
    // vez a quien se eligió y su caso. El globo que acaba de abrir el marcador se conserva.
    if (item?.dataset.caso) {
      // Si la pestaña cambia, el caso que se tenía elegido (con su barra de asignación y su línea de despacho) queda fuera de lo que
      // se verá: se suelta, sin tocar al sereno elegido ni su globo.
      const destino = pestanaDe(item.dataset.caso);
      if (destino && destino !== pestana) {
        if (sosSeleccionado) deseleccionar({ cerrarGlobo: false });
        soltarAsignado();
      }
      irAPestanaDe(item.dataset.caso, { cerrarGlobo: false });
    }
    if (item) {
      // Un filtro de la franja de resumen que lo esconde se quita: el sereno elegido en el mapa tiene que verse en la lista.
      if (desdeMapa && !item.offsetParent) fijarSolo("");
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

  // Con caso: la distancia a su propio caso (o «En el lugar», como el mapa) a la derecha de cada fila.
  function rotularConCaso(vinculados = vinculos()) {
    let alguna = false;
    for (const item of regionSerenos.querySelectorAll('[data-grupo-serenos="con-caso"] [data-sereno]')) {
      const vinculo = item.dataset.caso ? vinculados.get(item.dataset.caso) : null;
      const texto = vinculo?.sereno === item ? (vinculo.enElLugar ? "En el lugar" : distancia(vinculo.metros)) : "";
      fijarTexto(item.querySelector("[data-sereno-metros]"), texto);
      if (texto) alguna = true;
    }
    const columna = regionSerenos.querySelector('[data-columna="con-caso"]');
    if (columna) columna.hidden = !alguna;
  }

  // Un sereno bajo el cursor (o con el foco) se señala en el mapa con un anillo fino, sin mover el mapa. Solo con puntero fino.
  let serenoSenalado = null;
  function senalarSereno(id) {
    if (serenoSenalado === id) return;
    marcadoresSerenos.get(serenoSenalado)?.getElement()?.classList.remove("marcador--sobre");
    serenoSenalado = id;
    reponerSenal();
  }
  function reponerSenal() {
    if (serenoSenalado) marcadoresSerenos.get(serenoSenalado)?.getElement()?.classList.add("marcador--sobre");
  }
  const punteroFino = matchMedia("(hover: hover) and (pointer: fine)");
  regionSerenos.addEventListener("mouseover", (evento) => {
    if (!punteroFino.matches) return;
    const item = evento.target.closest("[data-sereno]");
    senalarSereno(item?.dataset.lat ? item.dataset.sereno : null);
  });
  regionSerenos.addEventListener("mouseleave", () => {
    senalarSereno(null);
    // El orden por cercanía que esperaba a que el cursor saliera.
    if (!ordenPendiente) return;
    ordenPendiente = false;
    calcularDespacho();
  });
  regionSerenos.addEventListener("focusin", (evento) => {
    const boton = evento.target.closest("[data-ver-sereno]");
    if (boton) senalarSereno(boton.closest("[data-sereno]").dataset.sereno);
  });
  regionSerenos.addEventListener("focusout", () => senalarSereno(null));

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
    // Buscando por nombre se ven todos los grupos con coincidencias (también «Fuera de servicio», que está plegado) y no queda
    // un título de grupo sin filas.
    panelSerenos.classList.toggle("serenos--buscando", Boolean(consulta));
    for (const grupo of regionSerenos.querySelectorAll("[data-grupo-serenos]"))
      grupo.classList.toggle("serenos__grupo--vacio", Boolean(consulta) && !grupo.querySelector("[data-sereno]:not(.sereno--oculto)"));
    // Lo que el servidor rehace y solo sabe el navegador: el grupo plegado y el filtro de la franja de resumen.
    sincronizarPlegado();
    if (panelSerenos.dataset.solo && !regionSerenos.querySelector(`[data-grupo-serenos="${panelSerenos.dataset.solo}"]`)) fijarSolo("");
  }
  filtroSerenos.addEventListener("input", aplicarFiltroSerenos);

  // «Fuera de servicio» va plegado; los otros dos grupos se ven siempre. Cada operador lo abre cuando lo necesita.
  function sincronizarPlegado() {
    const abierto = panelSerenos.classList.contains("serenos--fuera-abierto") || panelSerenos.classList.contains("serenos--buscando");
    regionSerenos.querySelector("[data-plegar-grupo]")?.setAttribute("aria-expanded", String(abierto));
  }
  regionSerenos.addEventListener("click", (evento) => {
    if (!evento.target.closest("[data-plegar-grupo]")) return;
    panelSerenos.classList.toggle("serenos--fuera-abierto");
    sincronizarPlegado();
  });

  // La franja de resumen filtra: una cifra deja solo ese grupo; volver a pulsarla los devuelve todos.
  const resumenSerenos = document.querySelector("[data-resumen-serenos]");
  function fijarSolo(grupo) {
    if (grupo) panelSerenos.dataset.solo = grupo;
    else delete panelSerenos.dataset.solo;
    for (const celda of resumenSerenos.querySelectorAll("[data-filtro-grupo]"))
      celda.setAttribute("aria-pressed", String(celda.dataset.filtroGrupo === grupo));
    if (grupo === "fuera") panelSerenos.classList.add("serenos--fuera-abierto");
    sincronizarPlegado();
  }
  resumenSerenos.addEventListener("click", (evento) => {
    const celda = evento.target.closest("[data-filtro-grupo]");
    if (!celda) return;
    const grupo = celda.dataset.filtroGrupo;
    // Una situación sin serenos no deja la lista en blanco.
    if (panelSerenos.dataset.solo !== grupo && !regionSerenos.querySelector(`[data-grupo-serenos="${grupo}"]`)) return;
    fijarSolo(panelSerenos.dataset.solo === grupo ? "" : grupo);
  });

  // La edad de la última posición («hace 18 s») sigue contando sola entre resincronizaciones. Parte de la edad que dice el
  // servidor (no del reloj del navegador, que puede estar desfasado) y se marca en ámbar pasado el umbral que él mismo indica.
  let origenEdades = new WeakMap();
  const textoEdad = (s) => (s < 60 ? `hace ${Math.floor(s)} s` : s < 3600 ? `hace ${Math.floor(s / 60)} min` : `hace ${Math.floor(s / 3600)} h`);
  function contarEdades() {
    const umbral = Number(regionSerenos.querySelector("[data-lista-serenos]")?.dataset.umbralAntigua) || 120;
    const ahora = performance.now();
    for (const el of regionSerenos.querySelectorAll("[data-edad-s]")) {
      const base = Number(el.dataset.edadS);
      let origen = origenEdades.get(el);
      if (!origen || origen.base !== base) origenEdades.set(el, origen = { base, desde: ahora });
      const segundos = base + (ahora - origen.desde) / 1000;
      fijarTexto(el.querySelector("[data-edad-texto]"), textoEdad(segundos));
      el.classList.toggle("sereno__edad--antigua", segundos >= umbral);
      el.querySelector(".sereno__edad-alerta")?.toggleAttribute("hidden", segundos < umbral);
    }
  }
  // Con el servidor recién resincronizado, la cuenta parte de cero otra vez.
  function reiniciarEdades() {
    origenEdades = new WeakMap();
    contarEdades();
  }
  setInterval(() => { if (!panelSerenos.hidden) contarEdades(); }, 5000);

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

  // ---------- Panel de serenos y capas del mapa ----------
  // 05/11/2026: el panel de serenos se abre encima del mapa cuando se pide (su cifra en la cabecera o en el mapa, o un sereno
  // tocado en el mapa) y lo tapa sin moverlo ni reencuadrarlo. Cada operador lo encuentra como lo dejó (solo en su navegador).
  const panelSerenos = document.getElementById("panel-serenos");
  const CLAVE_SERENOS = "cad-central-serenos";
  function mostrarSerenos(abierto, { devolverFoco = null, recordar = true } = {}) {
    panelSerenos.hidden = !abierto;
    if (abierto) contarEdades();
    for (const boton of document.querySelectorAll("[data-abrir-serenos]")) boton.setAttribute("aria-expanded", String(abierto));
    if (recordar) { try { localStorage.setItem(CLAVE_SERENOS, abierto ? "abierto" : "cerrado"); } catch { /* sin almacenamiento */ } }
    devolverFoco?.focus();
  }
  for (const boton of document.querySelectorAll("[data-abrir-serenos]")) {
    boton.addEventListener("click", () => {
      mostrarSerenos(panelSerenos.hidden);
      if (!panelSerenos.hidden) panelSerenos.querySelector("[data-cerrar-serenos]").focus();
    });
  }
  panelSerenos.querySelector("[data-cerrar-serenos]").addEventListener("click", () =>
    mostrarSerenos(false, { devolverFoco: document.querySelector(".mapa-herramientas [data-abrir-serenos]") }));
  panelSerenos.addEventListener("keydown", (evento) => {
    if (evento.key !== "Escape" || evento.defaultPrevented) return;
    evento.preventDefault();
    mostrarSerenos(false, { devolverFoco: document.querySelector(".mapa-herramientas [data-abrir-serenos]") });
  });
  try { if (localStorage.getItem(CLAVE_SERENOS) === "abierto") mostrarSerenos(true, { recordar: false }); } catch { /* sin almacenamiento */ }
  const central = document.querySelector(".central");

  // ---------- Pestañas de la bandeja (05/11/2026) ----------
  // SOS, reportes (por revisar y por asignar) y en curso. La elegida decide qué casos se ven en el mapa (central.css, por
  // data-pestana); los serenos se ven siempre. Si llega algo a otra pestaña, esa pestaña lo avisa con un punto; un SOS nuevo,
  // además, trae su pestaña si el operador no está con otro caso. Cada operador la encuentra como la dejó.
  const pestanas = [...document.querySelectorAll("[data-pestana]")];
  const CLAVE_PESTANA = "cad-central-pestana";
  const cuerpoCola = document.getElementById("cuerpo-cola");
  function pestanaDe(id) {
    if (!id) return null;
    const clave = CSS.escape(id);
    if (region.querySelector(`#sos-${clave}`)) return "sos";
    if (regionReportes?.querySelector(`#sos-${clave}`) || document.getElementById(`revisar-${id}`)) return "reportes";
    if (document.getElementById(`asignado-${id}`)) return "en-curso";
    const enMapa = regionCasosMapa?.querySelector(`[data-caso-mapa="${clave}"]`);
    if (!enMapa) return null;
    return enMapa.dataset.estado === "Recibido" || enMapa.dataset.estado === "En revisión" ? "reportes" : "en-curso";
  }
  function elegirPestana(nombre, { enfocar = false, soltar = true, cerrarGlobo = true } = {}) {
    if (!pestanas.length || !pestanas.some((b) => b.dataset.pestana === nombre)) return;
    const cambia = nombre !== pestana;
    pestana = nombre;
    for (const boton of pestanas) {
      const elegida = boton.dataset.pestana === nombre;
      boton.setAttribute("aria-selected", String(elegida));
      boton.tabIndex = elegida ? 0 : -1;
      if (elegida) boton.classList.remove("bandeja__pestana--aviso");
      if (elegida && enfocar) boton.focus();
    }
    for (const grupo of document.querySelectorAll("[data-grupo-pestana]")) grupo.hidden = grupo.dataset.grupoPestana !== nombre;
    central.dataset.pestana = nombre;
    try { localStorage.setItem(CLAVE_PESTANA, nombre); } catch { /* sin almacenamiento: vale para esta visita */ }
    if (!cambia) return;
    cuerpoCola.scrollTop = 0;
    // El globo de un caso que la nueva pestaña oculta tampoco queda a la vista (con su enlace).
    if (cerrarGlobo) mapa.closePopup();
    // Lo elegido en otra pestaña ya no se ve en el mapa: se suelta (el panel del caso, si está abierto, sigue).
    if (soltar) soltarTodo();
  }
  // Elegir un caso de otra pestaña (desde el panel, un enlace o el código) lleva a su pestaña, sin soltarlo; si está en una
  // situación plegada, la abre.
  function irAPestanaDe(id, { cerrarGlobo = true } = {}) {
    const suya = pestanaDe(id);
    if (suya && suya !== pestana) elegirPestana(suya, { soltar: false, cerrarGlobo });
    const plegada = document.querySelector(`[data-cuerpo-seccion][hidden]:has([data-sos="${CSS.escape(id)}"], [data-revisar="${CSS.escape(id)}"])`);
    if (plegada) plegarSeccion(plegada.dataset.cuerpoSeccion, false);
  }

  // Situaciones plegables de la pestaña «Reportes» (06/11/2026): «Por revisar» y «Por asignar» se pliegan desde su título para
  // llegar a un caso de la otra sin recorrer la primera. Cada operador las encuentra como las dejó.
  const CLAVE_SECCIONES = "cad-central-secciones";
  let seccionesPlegadas = new Set();
  try { seccionesPlegadas = new Set(JSON.parse(localStorage.getItem(CLAVE_SECCIONES) ?? "[]")); } catch { /* sin almacenamiento */ }
  function plegarSeccion(clave, plegada) {
    const cuerpo = document.querySelector(`[data-cuerpo-seccion="${CSS.escape(clave)}"]`);
    if (!cuerpo) return;
    cuerpo.hidden = plegada;
    document.querySelector(`[data-plegar-seccion="${CSS.escape(clave)}"]`)?.setAttribute("aria-expanded", String(!plegada));
    if (plegada) seccionesPlegadas.add(clave);
    else seccionesPlegadas.delete(clave);
    try { localStorage.setItem(CLAVE_SECCIONES, JSON.stringify([...seccionesPlegadas])); } catch { /* sin almacenamiento */ }
  }
  for (const clave of seccionesPlegadas) plegarSeccion(clave, true);
  document.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-plegar-seccion]");
    if (boton) plegarSeccion(boton.dataset.plegarSeccion, boton.getAttribute("aria-expanded") === "true");
  });
  function avisarPestana(nombre) {
    if (nombre !== pestana) pestanas.find((b) => b.dataset.pestana === nombre)?.classList.add("bandeja__pestana--aviso");
  }
  function contarPestanas() {
    const total = (selector, raiz) => Number(raiz?.querySelector(selector)?.dataset.total) || 0;
    const cifras = {
      sos: total("[data-lista-sos]", region),
      reportes: total("[data-lista-por-revisar]", regionRevisar) + total("[data-lista-reportes]", regionReportes),
      "en-curso": document.querySelectorAll("[data-asignado]").length,
    };
    for (const [nombre, valor] of Object.entries(cifras)) {
      const cifra = document.querySelector(`[data-cifra-pestana="${nombre}"]`);
      if (cifra) cadMovimiento.fijarCifra(cifra, valor);
    }
  }
  if (pestanas.length) {
    document.querySelector("[data-pestanas]").hidden = false;
    for (const boton of pestanas) boton.addEventListener("click", () => elegirPestana(boton.dataset.pestana));
    // Flechas, Inicio y Fin entre pestañas (patrón de pestañas de WAI-ARIA, activación automática).
    document.querySelector("[data-pestanas]").addEventListener("keydown", (evento) => {
      const i = pestanas.findIndex((b) => b.dataset.pestana === pestana);
      const destino = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: pestanas.length - 1 }[evento.key];
      if (destino === undefined) return;
      evento.preventDefault();
      elegirPestana(pestanas[(destino + pestanas.length) % pestanas.length].dataset.pestana, { enfocar: true });
    });
    // «N SOS» de la cabecera lleva a la pestaña de los SOS.
    for (const enlace of document.querySelectorAll("[data-ir-sos]")) enlace.addEventListener("click", () => elegirPestana("sos"));
    let guardada = null;
    try { guardada = localStorage.getItem(CLAVE_PESTANA); } catch { /* sin almacenamiento */ }
    // «/panel/central?pestana=reportes» (la antigua sección «Reportes», «Volver» desde un reporte): abre esa pestaña.
    const pedida = new URLSearchParams(location.search).get("pestana");
    if (pedida && pestanas.some((b) => b.dataset.pestana === pedida)) {
      guardada = pedida;
      history.replaceState(null, "", location.pathname);
    }
    elegirPestana(guardada ?? "sos", { soltar: false });
    if (central.dataset.pestana !== (guardada ?? "sos")) elegirPestana("sos", { soltar: false });
    contarPestanas();
  }
  // La leyenda y el filtro de la cola mandan sobre la misma capa: el chip siempre dice lo que se ve.
  function fijarCapa(capa, visible) {
    document.querySelector(`[data-capa="${capa}"]`)?.setAttribute("aria-pressed", String(visible));
    central.classList.toggle(`central--sin-${capa}`, !visible);
    if (capa === "sereno") lineaDespacho?.getElement()?.classList.toggle("visualmente-oculto", !visible);
  }
  for (const boton of document.querySelectorAll("[data-capa]")) {
    boton.addEventListener("click", () => fijarCapa(boton.dataset.capa, boton.getAttribute("aria-pressed") !== "true"));
  }

  // ---------- Clave del mapa (05/11/2026): cada icono y cada línea, tal como se ven ----------
  // Las muestras salen de marcadores.js, las mismas piezas que los marcadores del mapa: si cambia un icono, cambia su clave.
  const muestras = {
    sos: cadMarcadores.SIRENA,
    reporte: cadMarcadores.DOCUMENTO,
    sereno: cadMarcadores.escudo(true),
    "sereno-ocupado": cadMarcadores.escudo(false),
    "en-el-lugar": `<span class="marcador-sos marcador-caso--en-curso leyenda-icono__caso">${cadMarcadores.SIRENA}</span>`
      + `<span class="marcador-sereno leyenda-icono__sereno">${cadMarcadores.escudo(true)}</span>`,
  };
  for (const el of document.querySelectorAll("[data-muestra]")) el.innerHTML = muestras[el.dataset.muestra] ?? "";

  const abrirClave = document.querySelector("[data-abrir-clave]");
  const clave = document.querySelector("[data-clave-mapa]");
  function mostrarClave(abierta, { devolverFoco = false } = {}) {
    clave.hidden = !abierta;
    abrirClave.setAttribute("aria-expanded", String(abierta));
    if (!abierta && devolverFoco) abrirClave.focus();
  }
  abrirClave?.addEventListener("click", () => mostrarClave(clave.hidden));
  clave?.querySelector("[data-cerrar-clave]").addEventListener("click", () => mostrarClave(false, { devolverFoco: true }));
  // Esc la cierra si el foco está en ella o en su botón; un clic en el mapa también (la clave no tapa nada que se use a la vez).
  for (const el of [clave, abrirClave]) {
    el?.addEventListener("keydown", (evento) => {
      if (evento.key !== "Escape" || clave.hidden) return;
      evento.preventDefault();
      mostrarClave(false, { devolverFoco: true });
    });
  }
  mapa.on("click", () => { if (clave && !clave.hidden) mostrarClave(false); });

  // HU19-CA03: se vuelve a pedir el fragmento al servidor (fuente de verdad); se conserva la selección.
  async function refrescarSerenos() {
    const respuesta = await fetch("/panel/central?handler=Serenos", { headers: { Accept: "text/html" }, credentials: "same-origin" });
    if (respuesta.redirected || !respuesta.ok) return false;
    const seleccionado = regionSerenos.querySelector(".sereno--seleccionado")?.dataset.sereno;
    // El orden por cercanía de los disponibles: el parche devuelve el orden del servidor (por nombre) y, con el cursor sobre la lista,
    // no se reordena hasta que salga; mientras tanto se conserva el que el operador tenía a la vista.
    const ordenVisto = [...regionSerenos.querySelectorAll('[data-grupo-serenos="disponibles"] [data-sereno]')].map((fila) => fila.dataset.sereno);
    // Lo que solo sabe el navegador (elegido, sugerido, «Asignar» a la vista) se repone antes de medir: así la tarjeta no
    // cambia de alto por un instante ni parece moverse.
    const html = await respuesta.text();
    avisarSerenoLibre(html);
    cadMovimiento.actualizarRegion(regionSerenos, html, {
      animar: true,
      despues: () => {
        if (seleccionado) marcarSereno(seleccionado);
        aplicarFiltroSerenos();
        actualizarAsignacion();
        // Sin animar el orden aquí: actualizarRegion ya desliza lo que cambia de sitio al resincronizar.
        calcularDespacho({ animar: false });
        if (ordenPendiente && regionSerenos.matches(":hover")) conservarOrden(ordenVisto);
        rotularConCaso();
        reiniciarEdades();
      },
    });
    dibujarEnCurso();
    reponerSenal();
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
    const lista = regionSerenos.querySelector("[data-lista-serenos]");
    const operativos = Number(lista?.dataset.operativos) || 0;
    const registrados = Number(lista?.dataset.registrados) || 0;
    fijarIndicador("sos", sos);
    document.querySelector('[data-indicador="sos"]')?.classList.toggle("indicador--activo", sos > 0);
    // Los activos (operativos) de los registrados; cuántos están disponibles se ve en el panel de serenos.
    fijarIndicador("serenos-libres", operativos, `de ${registrados} ${registrados === 1 ? "sereno activo" : "serenos activos"}`);
    cadMovimiento.fijarCifra(document.querySelector("[data-serenos-activos]"), operativos);
    cadMovimiento.fijarCifra(document.querySelector("[data-serenos-registrados]"), registrados);
    // La franja de resumen del panel de serenos: disponibles, con caso y fuera de servicio.
    for (const [grupo, dato] of [["disponibles", "disponibles"], ["con-caso", "conCaso"], ["fuera", "fuera"]]) {
      if (lista) cadMovimiento.fijarCifra(document.querySelector(`[data-cifra="${grupo}"]`), Number(lista.dataset[dato]) || 0);
    }
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

  // Sonidos de 07/11/2026. Se comparan los casos en curso (el mapa) con los de la consulta anterior: la primera no suena.
  // Si en una misma consulta cambian varios, suena el de la etapa más avanzada. El reporte nuevo por revisar suena donde se
  // actualiza su lista (refrescar), porque un reporte sin ubicación del hecho no está en el mapa.
  const documento = (html) => new DOMParser().parseFromString(html, "text/html");
  let casosPrevios = null;
  function avisarCambiosDeCasos(html) {
    const actuales = new Map();
    for (const caso of documento(html).querySelectorAll("[data-caso-mapa]"))
      actuales.set(caso.dataset.casoMapa, { origen: caso.dataset.origen, estado: caso.dataset.estado });
    const previos = casosPrevios;
    casosPrevios = actuales;
    if (!previos) return;
    const sonidos = new Set();
    for (const [id, caso] of actuales) {
      const antes = previos.get(id);
      if (antes && antes.estado !== caso.estado) {
        if (caso.estado === "En atención") sonidos.add("atencion-iniciada");
        else if (caso.estado === "En camino") sonidos.add("sereno-en-camino");
      }
    }
    const sonido = ["atencion-iniciada", "sereno-en-camino"].find((s) => sonidos.has(s));
    if (sonido) CadSonidos.reproducir(sonido);
  }

  // Un sereno que queda disponible cuando hay SOS o reportes sin asignar: la central puede asignarle uno ya.
  let librePrevios = null;
  function avisarSerenoLibre(html) {
    const libres = new Set([...documento(html).querySelectorAll('[data-sereno][data-disponible="true"]')].map((el) => el.dataset.sereno));
    const previos = librePrevios;
    librePrevios = libres;
    if (!previos) return;
    const pendientes = (Number(region.querySelector("[data-lista-sos]")?.dataset.total) || 0)
      + (Number(regionReportes?.querySelector("[data-lista-reportes]")?.dataset.total) || 0);
    if (pendientes > 0 && [...libres].some((id) => !previos.has(id))) CadSonidos.reproducir("sereno-disponible-con-pendientes");
  }

  function marcarComoVisto() {
    sinVer = 0;
    document.title = tituloBase;
  }
  window.addEventListener("focus", marcarComoVisto);

  // ---------- Resincronización desde la base de datos ----------
  let cargado = false;
  async function refrescar() {
    let reporteLlegado = null;
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
        if (cargado && llegados.length) reporteLlegado = llegados.at(-1);
        for (const id of llegados) {
          const tarjeta = document.getElementById(`sos-${id}`);
          tarjeta?.classList.add("sos--llegada");
          tarjeta?.addEventListener("animationend", () => tarjeta.classList.remove("sos--llegada"), { once: true });
        }
        cadMovimiento.fijarCifra(contadorReportes, regionReportes.querySelector("[data-lista-reportes]").dataset.total);
        avisarReportesNuevos();
      }
    }
    if (regionRevisar) {
      const revisar = await fetch("/panel/central?handler=PorRevisar", { headers: { Accept: "text/html" }, credentials: "same-origin" });
      if (revisar.ok && !revisar.redirected) {
        const { nuevos: llegados } = cadMovimiento.actualizarRegion(regionRevisar, await revisar.text(), { animar: true, despues: () => marcarAsignado(casoConRuta) });
        // El reporte que llega a revisar entra con el mismo anillo que un reporte admitido (y el sonar en el mapa).
        if (cargado) {
          if (llegados.length) CadSonidos.reproducir("reporte-por-revisar");
          for (const id of llegados) {
            const tarjeta = document.getElementById(`revisar-${id}`);
            tarjeta?.classList.add("sos--llegada");
            tarjeta?.addEventListener("animationend", () => tarjeta.classList.remove("sos--llegada"), { once: true });
          }
        }
        cadMovimiento.fijarCifra(contadorRevisar, regionRevisar.querySelector("[data-lista-por-revisar]").dataset.total);
      }
    }
    if (regionAsignados) {
      const asignados = await fetch("/panel/central?handler=Asignados", { headers: { Accept: "text/html" }, credentials: "same-origin" });
      if (asignados.ok && !asignados.redirected)
        cadMovimiento.actualizarRegion(regionAsignados, await asignados.text(), { animar: true, despues: () => marcarAsignado(casoConRuta) });
    }
    if (regionCasosMapa) {
      const enCurso = await fetch("/panel/central?handler=CasosMapa", { headers: { Accept: "text/html" }, credentials: "same-origin" });
      if (enCurso.ok && !enCurso.redirected) {
        const html = await enCurso.text();
        avisarCambiosDeCasos(html);
        const { nuevos: entrantes } = cadMovimiento.actualizarRegion(regionCasosMapa, html);
        // El reporte por revisar que llega desde la app también es un caso llegado: se enfoca y lleva el sonar como un SOS.
        // Solo cuenta el que aparece en Recibido o En revisión (no el SOS que pasa a en curso al asignarlo).
        const porRevisar = entrantes.filter((clave) => {
          const el = regionCasosMapa.querySelector(`[data-caso-mapa="${CSS.escape(clave)}"]`);
          return el?.dataset.origen === "Reporte" && (el.dataset.estado === "Recibido" || el.dataset.estado === "En revisión");
        });
        if (cargado && porRevisar.length) reporteLlegado = porRevisar.at(-1);
      }
    }
    // El caso con recorrido que ya no está en curso (se cerró) deja de tenerlo.
    if (casoConRuta && !regionCasosMapa?.querySelector(`[data-caso-mapa="${CSS.escape(casoConRuta)}"]`)) casoConRuta = null;
    dibujarMarcadores();
    dibujarEnCurso();
    if (sosSeleccionado) resaltar(marcadores, sosSeleccionado);
    if (casoConRuta) resaltar(marcadoresEnCurso, casoConRuta);
    actualizarAsignacion();
    actualizarIndicadores();
    contarAsignados();
    contarPestanas();
    // Un reporte llegado con la central abierta releva al último caso del mapa (la primera carga no cuenta como llegada).
    // Si se mira otra pestaña, no se lleva el mapa a un caso que no se ve: la pestaña «Reportes» lo avisa.
    if (reporteLlegado && pestana === "reportes") destacarUltimo(reporteLlegado, { reporte: true });
    else if (reporteLlegado) avisarPestana("reportes");
    cargado = true;
    return true;
  }

  // ---------- Conexión en tiempo real ----------
  // Sonidos de 07/11/2026: suena una vez al perder la conexión y otra al recuperarla (no al conectar por primera vez).
  let estuvoConectado = false, enCorte = false;
  function estadoConexion(estado, texto) {
    indicador.className = `conexion conexion--${estado}`;
    indicador.dataset.estado = estado;
    indicador.querySelector("[data-texto]").textContent = texto;
    if (estado === "conectado") {
      estuvoConectado = true;
      if (enCorte) { enCorte = false; CadSonidos.reproducir("conexion-recuperada"); }
    } else if (estuvoConectado && !enCorte && (estado === "reconectando" || estado === "desconectado")) {
      enCorte = true;
      CadSonidos.reproducir("conexion-perdida");
    }
  }

  const conexion = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/central")
    .withAutomaticReconnect([0, 2000, 5000, 10000, 15000])
    .build();

  conexion.on("SosRecibido", async ({ id }) => {
    if (!(await refrescar())) return;
    avisar(id);
    // Un SOS nuevo trae su pestaña si el operador no está con otro caso; si lo está, «SOS» lo avisa y el mapa no se mueve.
    if (pestana !== "sos" && !panelAbierto() && !sosSeleccionado && !casoConRuta) elegirPestana("sos", { soltar: false });
    if (pestana === "sos") destacarUltimo(id);
    else avisarPestana("sos");
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
    return `<a class="boton ${principal ? "boton--principal" : "boton--secundario"} boton--compacto" href="${escapar(href)}" data-abrir-panel>${escapar(texto)}</a>`;
  }

  // Detalle de un caso pendiente: informativo, con el sereno sugerido si el caso está elegido. La acción es elegirlo (ya
  // hecho al tocarlo) y asignar desde «Serenos»; aquí solo se puede abrir su detalle.
  function fichaPendiente(item) {
    const reporte = item.dataset.origen === "Reporte";
    const sugerido = sosSeleccionado === item.dataset.sos ? sugerencia : null;
    return ficha({
      // El mismo estado y las mismas palabras que la tarjeta de la cola (un reporte de la cola está «Admitido»; un SOS, «Pendiente»).
      tipo: reporte ? "reporte" : "sos", etiqueta: reporte ? "Reporte" : "SOS", estado: reporte ? "Admitido" : "Pendiente",
      hora: `${reporte ? "a las" : "Registrado"} ${item.dataset.hora}`, titulo: item.dataset.ciudadano,
      datos: [
        ["Tipo", reporte ? item.dataset.categoria : item.dataset.clase],
        ["Ubicación", reporte ? "" : `Del dispositivo · ± ${item.dataset.precision} m`],
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
  // ---------- Panel del caso (05/11/2026) ----------
  // El detalle del caso elegido se abre en un marco encima del mapa (no lo mueve). Los enlaces y formularios marcados con
  // data-abrir-panel lo cargan ahí; sin JavaScript siguen yendo a la página del caso. Tras cada paso, la página del panel
  // avisa (panel-caso.js): la central rehace la bandeja y, si el caso ya salió del trabajo pendiente, cierra el panel y
  // confirma lo hecho en un aviso breve.
  const panelCaso = document.querySelector("[data-panel-caso]");
  const marcoCaso = panelCaso.querySelector("[data-marco-caso]");
  const tituloPanel = panelCaso.querySelector("[data-panel-titulo]");
  const avisoFlotante = document.querySelector("[data-aviso-flotante]");
  const ESTADOS_PENDIENTES = new Set(["Recibido", "En revisión", "Pendiente", "Admitido"]);
  let casoEnPanel = null;
  let origenPanel = null;

  const idDeRuta = (href) => href?.match(/\/panel\/(?:reportes|central\/sos)\/([0-9a-f-]{36})/i)?.[1] ?? null;

  // 05/11/2026: con el panel abierto, elegir otro caso (su tarjeta o su marcador) lo carga en el panel.
  function detalleDe(id) {
    return document.getElementById(`sos-${id}`)?.dataset.detalle
      ?? regionCasosMapa?.querySelector(`[data-caso-mapa="${CSS.escape(id)}"]`)?.dataset.detalle
      ?? document.querySelector(`#asignado-${CSS.escape(id)} a[data-abrir-panel]`)?.getAttribute("href")
      ?? (document.getElementById(`revisar-${id}`) ? `/panel/reportes/${id}` : null);
  }

  function seguirEnPanel(id) {
    if (!panelCaso || panelCaso.hidden || !id || id === casoEnPanel) return;
    const href = detalleDe(id);
    if (!href) return;
    casoEnPanel = id;
    marcoCaso.src = href;
  }

  function abrirPanel(href, { cargar = true } = {}) {
    origenPanel = document.activeElement;
    casoEnPanel = idDeRuta(href);
    if (cargar) marcoCaso.src = href;
    panelCaso.hidden = false;
    central.classList.add("central--con-panel");
    // El caso se marca en el mapa (de la cola, con su sereno sugerido; en curso o por revisar, con su marcador).
    if (casoEnPanel && document.getElementById(`sos-${casoEnPanel}`)) seleccionar(casoEnPanel, { conSereno: true });
    else if (casoEnPanel && (marcadoresEnCurso.has(casoEnPanel) || document.getElementById(`asignado-${casoEnPanel}`))) seleccionarAsignado(casoEnPanel);
  }

  function cerrarPanel({ devolverFoco = true } = {}) {
    if (panelCaso.hidden) return;
    panelCaso.hidden = true;
    central.classList.remove("central--con-panel");
    marcoCaso.removeAttribute("src");
    casoEnPanel = null;
    soltarTodo();
    if (devolverFoco) origenPanel?.isConnected ? origenPanel.focus() : document.getElementById("titulo-cola")?.focus();
  }

  function avisarBreve(texto) {
    avisoFlotante.textContent = texto;
    avisoFlotante.hidden = false;
    clearTimeout(avisarBreve.temporizador);
    avisarBreve.temporizador = setTimeout(() => { avisoFlotante.hidden = true; }, 6000);
  }

  document.addEventListener("click", (evento) => {
    const enlace = evento.target.closest("a[data-abrir-panel]");
    if (!enlace || evento.ctrlKey || evento.metaKey || evento.shiftKey || evento.button !== 0) return;
    evento.preventDefault();
    mapa.closePopup();
    abrirPanel(enlace.getAttribute("href"));
  });
  // «Revisar» envía su formulario al marco: la revisión se inicia y el reporte se abre en el panel.
  document.addEventListener("submit", (evento) => {
    const formulario = evento.target.closest("form[data-abrir-panel]");
    if (!formulario) return;
    formulario.target = "panel-caso"; // sin JavaScript se envía a la página del reporte, como siempre
    abrirPanel(formulario.getAttribute("action"), { cargar: false });
  });
  panelCaso.querySelector("[data-cerrar-panel]").addEventListener("click", () => cerrarPanel());
  document.addEventListener("keydown", (evento) => {
    if (evento.key !== "Escape" || panelCaso.hidden || evento.defaultPrevented) return;
    if (!panelSerenos.hidden || (clave && !clave.hidden) || !confirmacion.hidden) return;
    cerrarPanel();
  });
  window.addEventListener("message", async (evento) => {
    if (evento.origin !== location.origin || evento.source !== marcoCaso.contentWindow) return;
    // Esc dentro del panel (panel-caso.js lo reenvía si no hay nada a medias).
    if (evento.data?.tipo === "cad-panel-cerrar") { cerrarPanel(); return; }
    if (evento.data?.tipo !== "cad-panel-caso") return;
    const { id, estado, mensaje, titulo } = evento.data;
    tituloPanel.textContent = titulo ? `${titulo.replace(/\s*·\s*Central CAD$/, "")} · ${estado}` : "Caso elegido";
    // Un caso que el panel acaba de registrar («Nuevo reporte»: antes no había caso en el panel) se lleva a la vista en el mapa.
    const recienRegistrado = Boolean(id) && !casoEnPanel;
    if (id) casoEnPanel = id;
    // Lo que se hizo en el panel cambia la bandeja: se rehace desde el servidor.
    if (mensaje) { await refrescar(); await refrescarSerenos(); }
    if (recienRegistrado && mensaje && document.getElementById(`sos-${id}`)) seleccionar(id, { conSereno: true });
    if (mensaje && !ESTADOS_PENDIENTES.has(estado)) {
      cerrarPanel({ devolverFoco: false });
      avisarBreve(mensaje);
      document.getElementById("titulo-cola")?.focus();
    }
  });

  window.cadCentral = { conexion, refrescar, refrescarSerenos, mapa, abrirPanel, cerrarPanel };
})();
