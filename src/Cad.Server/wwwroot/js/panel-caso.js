// Panel del caso (05/11/2026): el detalle de un SOS o de un reporte dentro de la central, en un marco sobre el mapa.
// La página es la misma (mismos formularios, misma sugerencia del asistente, mismos datos): aquí solo se reordena en una
// cabecera con el paso a paso, tres pestañas (Caso · Ciudadano · Historial y tiempos) y la acción del paso fija al pie.
// Tras cada carga avisa a la central con el estado del caso, para que rehaga la bandeja y cierre el panel si el caso ya salió
// del trabajo pendiente (asignado, derivado, descartado…). Las secciones se mueven, no se copian: lo que consola.js refresca
// en vivo sigue en su sitio.
(() => {
  if (document.body.dataset.enPanel !== "true") return;
  const consola = document.querySelector(".consola");
  const trabajo = consola?.querySelector(".consola__trabajo");
  if (!consola || !trabajo) return;
  const lateral = consola.querySelector(".consola__lateral");
  const expediente = consola.querySelector(".consola__expediente");
  const barra = document.querySelector(".consola__barra");

  // Los enlaces salen del panel (la página completa, arriba); los formularios se quedan dentro.
  document.addEventListener("click", (evento) => {
    const enlace = evento.target.closest("a[href]");
    if (!enlace || enlace.target || enlace.getAttribute("href").startsWith("#") || /^(tel|mailto|sms):/.test(enlace.getAttribute("href"))) return;
    enlace.target = "_top";
  }, true);

  // ---------- Reparto en pestañas ----------
  const de = (raiz, selector) => [...(raiz?.querySelectorAll(selector) ?? [])];
  const seccionDe = (id) => document.querySelector(`[aria-labelledby="${id}"]`);
  const historial = [
    ...de(trabajo, ":scope > [aria-labelledby='titulo-historial']"),
    ...de(expediente, ":scope > [aria-labelledby='titulo-revision'], :scope > [data-tiempos]"),
  ];
  const resumen = lateral?.querySelector(".consola__resumen");
  const progreso = lateral?.querySelector(".progreso");
  const ciudadano = de(lateral, ":scope > :not(.consola__pasos)");
  const siguiente = trabajo.querySelector(":scope > .siguiente");
  // El aviso de «el caso cambió mientras escribías» (consola.js) se conserva, sobre las pestañas.
  const cambio = trabajo.querySelector(":scope > .consola__cambio");
  // «Caso»: primero lo que se reportó (y la sugerencia del asistente), luego el lugar y la clasificación, y al final las
  // acciones secundarias (otras acciones, descartar).
  const deTrabajo = de(trabajo, ":scope > :not(.siguiente):not(.consola__cambio)").filter((el) => !historial.includes(el));
  const esHecho = (el) => el.matches(".hecho, [data-sugerencia], [data-atencion], [aria-labelledby='titulo-asignacion']");
  // Rediseño del 10/11/2026: «lo esencial» (ficha clave: prioridad, tipo y dónde está) va primero; la ubicación y la clasificación
  // completas se pliegan en «Detalle del registro»; «Otras acciones» y descartar pasan al pie, tras «Más acciones».
  const ficha = expediente?.querySelector(":scope > [data-ficha-clave]");
  const enDetalle = de(expediente, ":scope > [aria-labelledby='titulo-ubicacion'], :scope > [aria-labelledby='titulo-clasificacion'], :scope > [aria-labelledby='titulo-lugar'], :scope > [aria-labelledby='titulo-categoria']")
    .filter((el) => !historial.includes(el));
  const masAcciones = ficha ? deTrabajo.filter((el) => el.matches(".otras-acciones, .descarte")) : [];
  let detalle = null;
  if (ficha && enDetalle.length > 0) {
    detalle = document.createElement("details");
    detalle.className = "panel-caso__detalle";
    const resumenDetalle = document.createElement("summary");
    resumenDetalle.innerHTML = '<span class="panel-caso__detalle-titulo">Detalle del registro</span><small></small><svg class="icono" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="m6 9 6 6 6-6"/></svg>';
    resumenDetalle.querySelector("small").textContent = ficha.dataset.detalleResumen ?? "";
    detalle.append(resumenDetalle, ...enDetalle);
    if (detalle.querySelector(".campo__error, .aviso--error")) detalle.open = true;
  }
  // En un reporte, lo que contó el ciudadano va antes que el lugar; en un SOS, primero lo esencial.
  const fichaDespues = ficha?.hasAttribute("data-despues-del-hecho") ?? false;
  const caso = ficha
    ? [
        ...(fichaDespues ? [] : [ficha]),
        ...deTrabajo.filter(esHecho),
        ...(fichaDespues ? [ficha] : []),
        ...(detalle ? [detalle] : []),
        ...de(expediente, ":scope > *").filter((el) => el !== ficha && !historial.includes(el) && !enDetalle.includes(el)),
        ...deTrabajo.filter((el) => !esHecho(el) && !masAcciones.includes(el)),
      ]
    : [
        ...deTrabajo.filter(esHecho),
        ...de(expediente, ":scope > *").filter((el) => !historial.includes(el)),
        ...deTrabajo.filter((el) => !esHecho(el)),
      ];

  const pestanas = [
    { clave: "caso", nombre: "Caso", piezas: caso },
    { clave: "ciudadano", nombre: "Ciudadano", piezas: ciudadano },
    { clave: "historial", nombre: "Historial y tiempos", piezas: [...historial, ...(resumen ? [resumen] : [])] },
  ].filter((p) => p.piezas.length > 0);

  const lista = document.createElement("div");
  lista.className = "panel-caso__pestanas";
  lista.setAttribute("role", "tablist");
  lista.setAttribute("aria-label", "Información del caso");
  const cuerpo = document.createElement("div");
  cuerpo.className = "panel-caso__cuerpo";

  const botones = [];
  const paneles = [];
  for (const [i, pestana] of pestanas.entries()) {
    const boton = document.createElement("button");
    boton.type = "button";
    boton.id = `pestana-${pestana.clave}`;
    boton.setAttribute("role", "tab");
    boton.setAttribute("aria-controls", `panel-${pestana.clave}`);
    boton.textContent = pestana.nombre;
    lista.append(boton);
    botones.push(boton);
    const panel = document.createElement("div");
    panel.id = `panel-${pestana.clave}`;
    panel.className = "panel-caso__seccion";
    panel.setAttribute("role", "tabpanel");
    panel.setAttribute("aria-labelledby", boton.id);
    panel.tabIndex = -1;
    panel.append(...pestana.piezas);
    cuerpo.append(panel);
    paneles.push(panel);
    boton.addEventListener("click", () => activar(i));
  }
  // El expediente completo, en su página, fuera del panel.
  const expedienteEnlace = document.createElement("a");
  expedienteEnlace.className = "panel-caso__expediente";
  expedienteEnlace.href = location.pathname;
  expedienteEnlace.target = "_top";
  expedienteEnlace.textContent = "Expediente ↗";
  lista.append(expedienteEnlace);

  function activar(indice, { enfocar = false } = {}) {
    for (const [i, boton] of botones.entries()) {
      const elegido = i === indice;
      boton.setAttribute("aria-selected", String(elegido));
      boton.tabIndex = elegido ? 0 : -1;
      paneles[i].hidden = !elegido;
    }
    if (enfocar) botones[indice].focus();
    // El minimapa (Leaflet) recalcula su tamaño al volver a verse.
    window.dispatchEvent(new Event("resize"));
  }
  lista.addEventListener("keydown", (evento) => {
    const actual = botones.indexOf(document.activeElement);
    if (actual < 0) return;
    const siguienteIndice = { ArrowRight: actual + 1, ArrowLeft: actual - 1, Home: 0, End: botones.length - 1 }[evento.key];
    if (siguienteIndice === undefined) return;
    evento.preventDefault();
    activar((siguienteIndice + botones.length) % botones.length, { enfocar: true });
  });

  // Cabecera: el paso a paso va bajo el titular; el cuerpo, en pestañas; la acción del paso, fija al pie.
  if (progreso && barra) {
    barra.append(progreso);
    // El paso a paso, en una barra de segmentos con el paso y el que sigue (10/11/2026). La lista de pasos queda para el lector de
    // pantalla; la barra es solo visual.
    const pasos = [...progreso.querySelectorAll(".progreso__paso")];
    if (pasos.length > 1) {
      const textoDe = (paso) => (paso.querySelector(":scope > span:not(.progreso__marca)")?.textContent ?? "").replace(/\s*\(omitido\)\s*$/, "").trim();
      const indiceActual = pasos.findIndex((paso) => paso.classList.contains("progreso__paso--actual"));
      const hechos = pasos.filter((paso) => paso.classList.contains("progreso__paso--hecho") || paso.classList.contains("progreso__paso--omitido")).length;
      const llegado = indiceActual >= 0 ? indiceActual : hechos - 1;
      const paso = document.createElement("div");
      paso.className = "panel-caso__paso";
      paso.setAttribute("aria-hidden", "true");
      const segmentos = document.createElement("div");
      segmentos.className = "panel-caso__segmentos";
      for (const [i] of pasos.entries()) {
        const segmento = document.createElement("i");
        if (i < llegado) segmento.className = "panel-caso__segmento--hecho";
        else if (i === llegado) segmento.className = indiceActual >= 0 ? "panel-caso__segmento--actual" : "panel-caso__segmento--hecho";
        segmentos.append(segmento);
      }
      const texto = document.createElement("p");
      texto.className = "panel-caso__paso-texto";
      const izquierda = document.createElement("span");
      const nombre = document.createElement("b");
      nombre.textContent = textoDe(pasos[Math.max(0, llegado)]);
      izquierda.append(nombre, indiceActual >= 0 ? ` · paso ${indiceActual + 1} de ${pasos.length}` : " · completado");
      texto.append(izquierda);
      if (indiceActual >= 0 && indiceActual < pasos.length - 1) {
        const derecha = document.createElement("span");
        derecha.textContent = `Sigue: ${textoDe(pasos[indiceActual + 1])}`;
        texto.append(derecha);
      }
      paso.append(segmentos, texto);
      progreso.before(paso);
      progreso.classList.add("panel-caso__progreso-lista");
    }
  }
  const pie = document.createElement("div");
  pie.className = "panel-caso__pie";
  if (siguiente) pie.append(siguiente);
  consola.replaceChildren(...[lista, cambio, cuerpo, pie].filter(Boolean));
  if (lateral && !lateral.isConnected) lateral.remove();

  // ---------- La acción del paso se pliega (05/11/2026) ----------
  // Plegada queda solo su título, en una línea, y el caso se lee con todo el alto del panel. Se despliega sola si trae un
  // error o si el operador ya empezó a llenarla; cada operador la encuentra como la dejó (solo en esta pestaña del navegador).
  if (siguiente) {
    let cabecera = siguiente.querySelector(":scope > .siguiente__cabecera");
    if (!cabecera) {
      cabecera = document.createElement("div");
      cabecera.className = "siguiente__cabecera";
      const titulo = siguiente.querySelector(":scope > h2, :scope > .siguiente__titulo");
      if (titulo) cabecera.append(titulo);
      siguiente.prepend(cabecera);
    }
    const contenido = document.createElement("div");
    contenido.className = "panel-caso__accion";
    contenido.id = "accion-del-paso";
    contenido.append(...[...siguiente.childNodes].filter((n) => n !== cabecera));
    siguiente.append(contenido);
    // Una acción corta (asignar al sereno sugerido, iniciar la revisión) va siempre a la vista: no hay nada que plegar.
    const corta = siguiente.dataset.accionCorta === "true";
    siguiente.classList.toggle("siguiente--corta", corta);
    const plegar = document.createElement("button");
    plegar.type = "button";
    plegar.className = "panel-caso__plegar";
    plegar.setAttribute("aria-controls", contenido.id);
    if (!corta) cabecera.append(plegar);
    const CLAVE = "cad-panel-accion";
    function plegarAccion(plegada, { recordar = true } = {}) {
      contenido.hidden = plegada;
      pie.classList.toggle("panel-caso__pie--plegado", plegada);
      plegar.setAttribute("aria-expanded", String(!plegada));
      plegar.innerHTML = `<span>${plegada ? "Mostrar" : "Ocultar"}</span><svg class="icono" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="${plegada ? "m6 15 6-6 6 6" : "m6 9 6 6 6-6"}"/></svg>`;
      plegar.setAttribute("aria-label", `${plegada ? "Mostrar" : "Ocultar"} ${cabecera.querySelector(".siguiente__titulo, h2")?.textContent.trim() ?? "la acción"}`);
      if (recordar) { try { sessionStorage.setItem(CLAVE, plegada ? "plegada" : "abierta"); } catch { /* sin almacenamiento */ } }
    }
    plegar.addEventListener("click", () => plegarAccion(!contenido.hidden));
    // El título también la abre: es la parte que queda a la vista.
    cabecera.querySelector(".siguiente__titulo, h2")?.addEventListener("click", () => { if (contenido.hidden) plegarAccion(false); });
    let recordada = null;
    try { recordada = sessionStorage.getItem(CLAVE); } catch { /* sin almacenamiento */ }
    const hayError = contenido.querySelector(".campo__error, .aviso--error, [aria-invalid='true']");
    plegarAccion(!corta && recordada === "plegada" && !hayError, { recordar: false });
  }

  // ---------- «Más acciones» (10/11/2026) ----------
  // «Otras acciones» y descartar salen del cuerpo y se abren desde el pie, bajo la acción del paso. Se mueven, no se copian.
  let hojaMas = null;
  const abrirMas = (abrir) => {
    if (!hojaMas) return;
    hojaMas.hidden = !abrir;
    botonMas.setAttribute("aria-expanded", String(abrir));
    botonMas.querySelector("span").textContent = abrir ? "Menos acciones" : "Más acciones";
    if (abrir) hojaMas.scrollIntoView({ block: "nearest" });
  };
  let botonMas = null;
  if (masAcciones.length > 0) {
    hojaMas = document.createElement("div");
    hojaMas.className = "panel-caso__mas";
    hojaMas.id = "panel-mas-acciones";
    hojaMas.append(...masAcciones);
    botonMas = document.createElement("button");
    botonMas.type = "button";
    botonMas.className = "panel-caso__mas-boton";
    botonMas.setAttribute("aria-controls", hojaMas.id);
    botonMas.innerHTML = '<span>Más acciones</span><svg class="icono" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="m6 15 6-6 6 6"/></svg>';
    botonMas.addEventListener("click", () => abrirMas(hojaMas.hidden));
    const fila = document.createElement("div");
    fila.className = "panel-caso__mas-fila";
    fila.append(botonMas);
    pie.append(hojaMas, fila);
    abrirMas(Boolean(hojaMas.querySelector(".campo__error, .aviso--error, details[open]")));
  }

  // «Ver en el mapa»: la central lleva su mapa grande hasta el caso.
  document.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-ver-mapa]");
    if (!boton || window.parent === window) return;
    window.parent.postMessage({ tipo: "cad-panel-ver-mapa", lat: Number(boton.dataset.lat), lon: Number(boton.dataset.lon) }, location.origin);
  });

  // Un aviso de éxito queda en una línea y se retira solo: lo que pasó ya se ve en el paso a paso y en el pie.
  const exito = document.querySelector(".pagina--en-panel > .aviso--exito");
  if (exito) setTimeout(() => exito.remove(), 6000);

  // Abre la pestaña con un error de formulario; si no, «Caso».
  const conError = paneles.findIndex((p) => p.querySelector(".campo__error, .aviso--error"));
  activar(conError >= 0 ? conError : 0);

  // Un atajo que lleva a otra sección («Despachar ahora») abre antes su pestaña.
  document.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-ir-a]");
    const destino = boton && document.getElementById(boton.dataset.irA);
    const indice = destino ? paneles.findIndex((p) => p.contains(destino)) : -1;
    if (indice >= 0) activar(indice);
    else if (destino && hojaMas?.contains(destino)) {
      abrirMas(true);
      destino.closest("details")?.setAttribute("open", "");
      destino.scrollIntoView({ block: "nearest" });
    }
  }, true);

  // Esc dentro del panel también lo cierra (el teclado no sale del marco), salvo con un formulario a medias: lo escrito no se
  // pierde por una tecla.
  const aMedias = () => [...document.querySelectorAll(".consola form")].some((formulario) =>
    [...formulario.elements].some((campo) => {
      if (campo.type === "hidden" || campo.type === "submit" || campo.type === "button" || campo.disabled) return false;
      if (campo.type === "radio" || campo.type === "checkbox") return campo.checked !== campo.defaultChecked;
      // Sin una opción marcada en el HTML, el navegador muestra la primera: eso no es un cambio (la misma regla de consola.js).
      if (campo.tagName === "SELECT") return campo.selectedIndex !== Math.max(0, [...campo.options].findIndex((o) => o.defaultSelected));
      return "value" in campo && "defaultValue" in campo && campo.value !== campo.defaultValue;
    }));
  document.addEventListener("keydown", (evento) => {
    if (evento.key !== "Escape" || evento.defaultPrevented || window.parent === window || aMedias()) return;
    window.parent.postMessage({ tipo: "cad-panel-cerrar" }, location.origin);
  });

  // ---------- Aviso a la central ----------
  if (window.parent !== window) {
    const estado = (barra?.dataset.origenEstado ?? "").replace(/^(SOS|Reporte|Caso urgente)\s+/, "");
    const mensaje = document.querySelector(".pagina--en-panel > .aviso--exito .aviso__titulo")?.textContent.trim() ?? "";
    window.parent.postMessage({ tipo: "cad-panel-caso", id: barra?.dataset.caso ?? "", estado, mensaje, titulo: document.title }, location.origin);
  }
})();
