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
  const caso = [
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
  if (progreso && barra) barra.append(progreso);
  const pie = document.createElement("div");
  pie.className = "panel-caso__pie";
  if (siguiente) pie.append(siguiente);
  consola.replaceChildren(...[lista, cambio, cuerpo, pie].filter(Boolean));
  if (lateral && !lateral.isConnected) lateral.remove();

  // Abre la pestaña con un error de formulario; si no, «Caso».
  const conError = paneles.findIndex((p) => p.querySelector(".campo__error, .aviso--error"));
  activar(conError >= 0 ? conError : 0);

  // Un atajo que lleva a otra sección («Despachar ahora») abre antes su pestaña.
  document.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-ir-a]");
    const destino = boton && document.getElementById(boton.dataset.irA);
    const indice = destino ? paneles.findIndex((p) => p.contains(destino)) : -1;
    if (indice >= 0) activar(indice);
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
