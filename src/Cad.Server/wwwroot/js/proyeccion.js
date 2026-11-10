// HU46/HU48 (S6-T20) — mapa de la vista «Proyección». Rediseño del 05/11/2026.
// Cada zona de unos 550 m se pinta con los casos esperados (más claro, más casos) y su nivel es absoluto: casos esperados por bloque de
// 4 h. Las zonas de nivel alto y medio llevan además el nivel escrito (CA08: no solo color); una de nivel alto sin sereno operativo
// cerca lleva el borde ámbar discontinuo. Los serenos operativos salen en el mapa y, al elegir una zona, una línea llega al más cercano.
// La línea de tiempo cambia de bloque de 4 h sin volver al servidor. Solo celdas: el mapa no recibe ni muestra casos (CA03).
(() => {
  const contenedor = document.getElementById("mapa-proyeccion");
  if (!contenedor || typeof L === "undefined") return;
  const zonas = JSON.parse(document.getElementById("celdas-proyeccion")?.textContent || "[]");
  const ctx = JSON.parse(document.getElementById("contexto-proyeccion")?.textContent || "{}");
  const bloques = ctx.bloques || [];
  const serenos = ctx.serenos || [];
  const panel = document.querySelector(".focos__panel");
  const lineaTiempo = document.querySelector("[data-linea-tiempo]");
  const lista = document.getElementById("zonas-proyectadas");
  const textoBloque = document.querySelector("[data-vista-bloque]");
  const avisoTranquila = document.querySelector("[data-tranquila]");
  const reducido = () => window.cadMovimiento?.reducido() ?? matchMedia("(prefers-reduced-motion: reduce)").matches;

  const HIELO = "#E8F8FF", CELESTE = "#5CC8FF", AMBAR = "#F5B544";
  const cifra = new Intl.NumberFormat("es-PE", { maximumFractionDigits: 1, minimumFractionDigits: 1 });
  const umbral = new Intl.NumberFormat("es-PE", { maximumFractionDigits: 2 });
  const km = (metros) => `${cifra.format(metros / 1000)} km`;
  const escapar = (t) => String(t).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
  const ADVERTENCIA = '<svg class="icono" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M10.3 4.2 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0z"/><path d="M12 9.5v4M12 17h.01"/></svg>';

  L.Control.Attribution.prototype.options.prefix = false;
  const mapa = L.map(contenedor, { zoomControl: false }).setView([Number(contenedor.dataset.centroLat), Number(contenedor.dataset.centroLon)], Number(contenedor.dataset.zoom));
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);
  mapa.createPane("brillo").style.zIndex = 380;

  // De lejos las insignias solo llevan su número (las zonas son pequeñas y el nivel está en la lista); desde el zoom 15, también el nivel
  // escrito sobre la zona (CA08).
  const marco = contenedor.closest(".focos");
  const alejar = () => marco.classList.toggle("focos--lejos", mapa.getZoom() < 15);
  mapa.on("zoomend", alejar);

  // ---------- Estado ----------
  let bloque = -1; // -1 = la ventana entera; si no, el índice del bloque de 4 h
  let elegida = null;
  const cobertura = bloques.reduce((suma, b) => suma + b.fraccion, 0) || 1;

  // Lo esperado por bloque de 4 h (para pintar y para el nivel) y lo que se muestra en la lista (la ventana suma; el bloque, no).
  const porBloque = (z) => (bloque < 0 ? z.esperados / cobertura : z.bloques?.[bloque] ?? 0);
  const mostrado = (z) => (bloque < 0 ? z.esperados : z.bloques?.[bloque] ?? 0);
  const nivelDe = (z) => (bloque < 0 ? z.nivel : z.niveles?.[bloque] ?? "Bajo");
  const sinCobertura = (z) => nivelDe(z) === "Alto" && (z.metros == null || z.metros > ctx.umbralCobertura);
  const porQue = (z) => (bloque < 0 ? z.porQue : z.porQueBloques?.[bloque]);
  const centro = (z) => [(z.sur + z.norte) / 2, (z.oeste + z.este) / 2];
  const intensidad = (z) => Math.min(1, porBloque(z) / (2 * (ctx.umbralAlto || 1)));

  // ---------- Zonas ----------
  function estilo(z) {
    const t = intensidad(z);
    const nivel = nivelDe(z);
    const base = nivel === "Alto"
      ? { color: HIELO, fillColor: CELESTE, fillOpacity: 0.35 + 0.45 * t, weight: 2, opacity: 1 }
      : nivel === "Medio"
        ? { color: CELESTE, fillColor: CELESTE, fillOpacity: 0.18 + 0.22 * t, weight: 1.5, opacity: 0.9 }
        : { color: "#3D5A93", fillColor: CELESTE, fillOpacity: 0.03 + 0.12 * t, weight: 0.5, opacity: 0.5 };
    const sin = sinCobertura(z) ? { color: AMBAR, dashArray: "6 4", weight: 2.5 } : { dashArray: null };
    // La zona elegida se marca con un borde blanco más grueso, sea cual sea su nivel.
    return z.clave === elegida ? { ...base, ...sin, color: "#FFFFFF", weight: 3.5, opacity: 1 } : { ...base, ...sin };
  }

  const rectangulos = new Map();
  const limites = [];
  for (const z of zonas) {
    const caja = [[z.sur, z.oeste], [z.norte, z.este]];
    limites.push(...caja);
    const r = L.rectangle(caja, { className: "zona-foco", interactive: true, ...estilo(z) }).addTo(mapa);
    r.bindTooltip("", { sticky: true });
    r.on("click", () => elegir(z.clave));
    rectangulos.set(z.clave, r);
  }

  const textoZona = (z) => `${z.zona}: nivel ${nivelDe(z).toLowerCase()}, ${cifra.format(mostrado(z))} casos esperados${sinCobertura(z) ? " · sin cobertura cercana" : ""}`;

  // El brillo (zonas altas y medias, desenfocadas) y las insignias de orden y nivel.
  const brillo = L.layerGroup().addTo(mapa);
  const insignias = L.layerGroup().addTo(mapa);

  function vista() {
    return zonas.filter((z) => mostrado(z) > 0).sort((a, b) => mostrado(b) - mostrado(a));
  }

  function pintar() {
    brillo.clearLayers();
    insignias.clearLayers();
    const ordenadas = vista();
    const rango = new Map(ordenadas.slice(0, 10).map((z, i) => [z.clave, i + 1]));
    for (const z of zonas) {
      const r = rectangulos.get(z.clave);
      r.setStyle(estilo(z));
      r.setTooltipContent(escapar(textoZona(z)));
    }
    let etiquetadas = 0;
    for (const z of ordenadas) {
      const nivel = nivelDe(z);
      if (nivel === "Bajo") continue;
      L.rectangle([[z.sur, z.oeste], [z.norte, z.este]], {
        pane: "brillo", interactive: false, stroke: false, fillColor: CELESTE, fillOpacity: 0.35 + 0.6 * intensidad(z),
      }).addTo(brillo);
      if (!rango.has(z.clave) && etiquetadas >= 20) continue;
      if (!rango.has(z.clave)) etiquetadas++;
      const numero = rango.get(z.clave);
      const clases = ["insignia-foco", `insignia-foco--${nivel.toLowerCase()}`, sinCobertura(z) ? "insignia-foco--sin-cobertura" : "", z.clave === elegida ? "insignia-foco--elegida" : ""].filter(Boolean).join(" ");
      L.marker(centro(z), {
        interactive: false, keyboard: false, zIndexOffset: numero ? 500 - numero : 0,
        icon: L.divIcon({ className: "", html: `<span class="${clases}">${numero ? `<b>${numero}</b>` : ""}<span class="insignia-foco__nivel">${nivel}</span></span>`, iconSize: null }),
      }).addTo(insignias);
    }
    // Lo que se ve no es una tarjeta más: se dice qué se está mirando y, si nada llega a «Medio», que es una vista tranquila.
    if (textoBloque) {
      const b = bloques[bloque];
      const base = bloque < 0
        ? "Toda la ventana; el nivel es el promedio por bloque de 4 horas. Elige un bloque en la línea de tiempo para verlo solo."
        : `${b.dia}, de ${b.rotulo}: ${cifra.format(b.total)} casos esperados en toda la ciudad.`;
      const tranquila = !ordenadas.some((z) => nivelDe(z) !== "Bajo");
      // En la ventana entera el nivel es el promedio por bloque: si algún bloque sí llega al umbral, se dice y no se llama tranquila.
      const algunBloque = zonas.some((z) => z.niveles?.some((n) => n !== "Bajo"));
      const aviso = bloque >= 0 ? "en este bloque" : algunBloque ? "en promedio, pero algún bloque sí: míralo en la línea de tiempo" : "en ningún bloque";
      textoBloque.textContent = tranquila ? `${base} Ninguna zona llega a ${umbral.format(ctx.umbralMedio)} casos esperados ${aviso}.` : base;
    }
    // El aviso de la lista solo describe la ventana entera: con un bloque elegido lo dice el texto de arriba.
    if (avisoTranquila) avisoTranquila.hidden = bloque >= 0;
  }

  // ---------- Serenos ----------
  const marcasSerenos = [];
  for (const s of serenos) {
    const estadoSereno = s.disponible ? "disponible" : "atendiendo un caso";
    const m = L.marker([s.lat, s.lon], {
      icon: L.divIcon(window.cadMarcadores ? window.cadMarcadores.sereno(s.disponible) : { className: "", html: "" }),
      title: `${s.nombre} · ${estadoSereno}`, alt: `${s.nombre}, sereno ${estadoSereno}`,
      keyboard: false, zIndexOffset: 900,
      // Leaflet asigna el contenido de un tooltip con innerHTML: el nombre de la cuenta va escapado.
    }).bindTooltip(escapar(`${s.nombre} · ${estadoSereno}`)).addTo(mapa);
    marcasSerenos.push(m);
  }

  const metros = (a, b) => mapa.distance(a, b);
  const masCercano = (punto) => serenos.reduce((mejor, s) => {
    const d = metros(punto, [s.lat, s.lon]);
    return !mejor || d < mejor.d ? { s, d } : mejor;
  }, null);

  // ---------- Zona elegida: sonar una vez, línea al sereno más cercano y encuadre ----------
  const elegidaCapa = L.layerGroup().addTo(mapa);

  function relleno() {
    const enEscritorio = getComputedStyle(panel).position === "absolute";
    const ancho = enEscritorio ? panel.offsetWidth + 40 : 24;
    const abajo = enEscritorio && lineaTiempo ? lineaTiempo.offsetHeight + 40 : 24;
    return { paddingTopLeft: [ancho, 24], paddingBottomRight: [enEscritorio ? 72 : 24, abajo] };
  }

  function encuadrar(puntos, opciones = {}) {
    if (!puntos.length) return;
    const caja = L.latLngBounds(puntos);
    const cfg = { ...relleno(), maxZoom: 15, ...opciones };
    if (reducido() || opciones.animate === false) mapa.fitBounds(caja, { ...cfg, animate: false });
    else mapa.flyToBounds(caja, { ...cfg, duration: 0.9 });
  }

  function elegir(clave) {
    elegidaCapa.clearLayers();
    elegida = clave === elegida ? null : clave;
    const z = zonas.find((x) => x.clave === elegida);
    if (z) {
      const punto = centro(z);
      const cerca = masCercano(punto);
      const rojo = sinCobertura(z);
      if (cerca) {
        L.polyline([punto, [cerca.s.lat, cerca.s.lon]], { color: rojo ? AMBAR : HIELO, weight: 2.5, dashArray: "6 5", interactive: false }).addTo(elegidaCapa);
        const medio = [(punto[0] + cerca.s.lat) / 2, (punto[1] + cerca.s.lon) / 2];
        L.marker(medio, {
          interactive: false, keyboard: false,
          icon: L.divIcon({ className: "", html: `<span class="insignia-foco insignia-foco--distancia${rojo ? " insignia-foco--sin-cobertura" : ""}">${km(cerca.d)}</span>`, iconSize: null }),
        }).addTo(elegidaCapa);
      }
      L.marker(punto, {
        interactive: false, keyboard: false, zIndexOffset: -100,
        icon: L.divIcon({ className: "", html: '<span class="sonar-foco"><i></i><i></i></span>', iconSize: [120, 120], iconAnchor: [60, 60] }),
      }).addTo(elegidaCapa);
      encuadrar([[z.sur, z.oeste], [z.norte, z.este], ...(cerca ? [[cerca.s.lat, cerca.s.lon]] : [])], { maxZoom: 16 });
    } else {
      encuadrarTodo();
    }
    pintar();
    marcarElegida();
  }

  function marcarElegida() {
    for (const boton of document.querySelectorAll("[data-ver-zona]"))
      boton.setAttribute("aria-pressed", boton.dataset.verZona === elegida ? "true" : "false");
  }

  // ---------- Lista de zonas ----------
  // La misma fila que dibuja el servidor (_Proyeccion.cshtml): nivel, casos esperados, sereno cercano y una barra contra los umbrales.
  function filaLista(z, i) {
    const nivel = nivelDe(z);
    const sin = sinCobertura(z);
    const sereno = z.metros == null
      ? '<span class="texto-secundario">No hay serenos operativos con ubicación</span>'
      : `<span>${escapar(z.sereno)}, a ${km(z.metros)}</span>`;
    const ancho = Math.round(intensidad(z) * 1000) / 10;
    return `<li class="ranking__item${sin ? " ranking__item--sin-cobertura" : ""}" data-clave="${escapar(z.clave)}" data-zona="${escapar(z.clave)}" data-nivel="${nivel}">`
      + `<button type="button" class="ranking__boton" data-ver-zona="${escapar(z.clave)}" aria-pressed="false">`
      + `<span class="ranking__orden num" aria-hidden="true">${i + 1}</span>`
      + `<span class="ranking__nombre">${escapar(z.zona)}</span>`
      + `<span class="nivel-foco nivel-foco--${nivel.toLowerCase()}">${nivel}</span>`
      + `<span class="ranking__cifra num"><strong>${cifra.format(mostrado(z))}</strong><span class="visualmente-oculto"> esperados</span></span>`
      + `<span class="ranking__sereno">${sin ? `<strong class="proyeccion__sin-cobertura" data-sin-cobertura>${ADVERTENCIA}Sin cobertura cercana</strong>` : ""}${sereno}</span>`
      + `<span class="ranking__barra ranking__barra--nivel" aria-hidden="true"><i style="width:${ancho}%"></i></span>`
      + (porQue(z) ? `<span class="ranking__porque">${escapar(porQue(z))}</span>` : "")
      + "</button></li>";
  }

  // El resumen por niveles (Alto, Medio, Bajo y sin sereno) cuenta las zonas que muestra la lista en el bloque elegido.
  const resumenNiveles = document.querySelector("[data-resumen-niveles]");
  function actualizarResumen(visibles) {
    if (!resumenNiveles) return;
    const n = { Alto: 0, Medio: 0, Bajo: 0, sin: 0 };
    for (const z of visibles) {
      n[nivelDe(z)]++;
      if (sinCobertura(z)) n.sin++;
    }
    for (const el of resumenNiveles.querySelectorAll("[data-nivel-n]")) el.textContent = n[el.dataset.nivelN];
    resumenNiveles.querySelector(".niveles-resumen__celda--sin")?.classList.toggle("niveles-resumen__celda--aviso", n.sin > 0);
  }

  function dibujarLista() {
    if (!lista) return;
    const visibles = vista().slice(0, 10);
    const html = visibles.map(filaLista).join("");
    actualizarResumen(visibles);
    if (window.cadMovimiento) window.cadMovimiento.actualizarRegion(lista, html, { animar: true, despues: marcarElegida });
    else {
      lista.innerHTML = html;
      marcarElegida();
    }
  }

  lista?.addEventListener("click", (e) => {
    const boton = e.target.closest("[data-ver-zona]");
    if (boton) elegir(boton.dataset.verZona);
  });

  // ---------- Línea de tiempo ----------
  function elegirBloque(i) {
    if (i === bloque) return;
    bloque = i;
    for (const boton of document.querySelectorAll("[data-bloque]"))
      boton.setAttribute("aria-pressed", Number(boton.dataset.bloque) === bloque ? "true" : "false");
    // La zona elegida sigue elegida mientras haya casos esperados en el bloque.
    const z = zonas.find((x) => x.clave === elegida);
    if (z && mostrado(z) <= 0) {
      elegidaCapa.clearLayers();
      elegida = null;
    }
    pintar();
    dibujarLista();
  }

  lineaTiempo?.addEventListener("click", (e) => {
    const boton = e.target.closest("[data-bloque]");
    if (boton) elegirBloque(Number(boton.dataset.bloque));
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && elegida) elegir(elegida);
  });

  // ---------- Encuadre inicial ----------
  function encuadrarTodo() {
    const altas = zonas.filter((z) => nivelDe(z) !== "Bajo");
    const puntos = (altas.length ? altas : zonas).flatMap((z) => [[z.sur, z.oeste], [z.norte, z.este]]);
    const conSerenos = [...puntos, ...serenos.map((s) => [s.lat, s.lon])];
    encuadrar(conSerenos.length ? conSerenos : limites, { animate: false });
  }

  pintar();
  alejar();
  // El contenedor toma su tamaño final cuando termina de cargar la hoja de estilos: se recalcula antes de encuadrar las zonas.
  const iniciar = () => {
    mapa.invalidateSize();
    encuadrarTodo();
    alejar();
  };
  iniciar();
  if (document.readyState !== "complete") window.addEventListener("load", iniciar, { once: true });
  new ResizeObserver(() => mapa.invalidateSize()).observe(contenedor);

  // Acceso para la verificación automatizada (como window.cadCentral).
  window.cadProyeccion = { mapa, elegir, elegirBloque, zonas, ctx };
})();
