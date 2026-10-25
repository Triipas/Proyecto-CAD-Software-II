// Detalle de una sesión de acompañamiento (24/10/2026).
//  - Páginas de posiciones sin recargar: «Más recientes» / «Anteriores» piden la página y cambian solo la tabla. Sin
//    JavaScript son enlaces normales (?pagina=N).
//  - Con la sesión activa, la página se actualiza sola: el servidor avisa por el canal de la central cada vez que llega algo
//    del ciudadano (posición, confirmación, aviso o cierre) y, por si se pierde un aviso o pasa el umbral de «sin conexión»,
//    también se consulta cada 5 s; la antigüedad de las posiciones avanza cada segundo. Se vuelve a pedir la misma página y se cambian solo las partes marcadas con data-vivo.
//    Si el foco del operador está en una parte, esta se cambia igual y el foco vuelve al elemento equivalente; solo se deja
//    para después la parte en la que está escribiendo o la que muestra un error recién recibido.
(() => {
  const raiz = document.querySelector("[data-sesion]");
  if (!raiz) return;
  const sesion = raiz.dataset.sesion;
  let pagina = Number(new URLSearchParams(location.search).get("pagina")) || 1;
  let activa = raiz.dataset.activa === "true";
  let temporizador = null, conexion = null;

  // Una sola petición a la vez; las que llegan mientras tanto se juntan en una sola siguiente, que conserva la parte que
  // debe cambiarse sí o sí (la tabla al cambiar de página). Cada llamada devuelve la promesa de la petición que la atiende.
  let enCurso = null, siguiente = null;
  function refrescar({ forzar = null } = {}) {
    if (!enCurso) {
      enCurso = ejecutar(forzar).finally(() => {
        enCurso = null;
        if (siguiente) {
          const { forzar: f, resolver } = siguiente;
          siguiente = null;
          refrescar({ forzar: f }).then(resolver);
        }
      });
      return enCurso;
    }
    if (siguiente) {
      siguiente.forzar ??= forzar;
      return siguiente.promesa;
    }
    let resolver;
    const promesa = new Promise((r) => { resolver = r; });
    siguiente = { forzar, resolver, promesa };
    return promesa;
  }

  // Se deja para después solo lo que el operador perdería: un texto a medio escribir o un error que aún no ha leído.
  function aplazar(region) {
    const foco = document.activeElement;
    const escribiendo = foco && region.contains(foco) && foco.matches("input:not([type=hidden]), textarea, select");
    return escribiendo || region.querySelector("[data-error-confirmacion]") !== null;
  }

  // Cómo volver a encontrar el elemento con foco en la parte nueva.
  function huella(elemento) {
    if (elemento.id) return `#${CSS.escape(elemento.id)}`;
    if (elemento.dataset.pagina) return `[data-pagina="${CSS.escape(elemento.dataset.pagina)}"]`;
    if (elemento.matches("a[href]")) return `a[href="${CSS.escape(elemento.getAttribute("href"))}"]`;
    if (elemento.matches("button[type=submit]")) return "button[type=submit]";
    return null;
  }

  async function ejecutar(forzar) {
    let aplazadas = 0;
    try {
      const respuesta = await fetch(`${location.pathname}?pagina=${pagina}`, { credentials: "same-origin", headers: { Accept: "text/html" } });
      if (respuesta.redirected || !respuesta.ok) return; // sesión del panel terminada o error: se conserva lo mostrado
      const nuevo = new DOMParser().parseFromString(await respuesta.text(), "text/html");
      for (const region of document.querySelectorAll("[data-vivo]")) {
        const reemplazo = nuevo.querySelector(`[data-vivo="${region.dataset.vivo}"]`);
        if (!reemplazo) continue;
        if (region.dataset.vivo !== forzar && aplazar(region)) { aplazadas++; continue; }
        const foco = region.contains(document.activeElement) ? huella(document.activeElement) : null;
        const importada = document.importNode(reemplazo, true);
        region.replaceWith(importada);
        if (foco) importada.querySelector(foco)?.focus({ preventScroll: true });
      }
      const mapa = nuevo.getElementById("mapa-recorrido");
      if (mapa) window.CadRecorrido?.actualizar(mapa.dataset.puntos, mapa.dataset.actual === "true");
      // La sesión terminó: se deja de actualizar solo cuando ya no queda ninguna parte por poner al día.
      if (nuevo.querySelector("[data-sesion]")?.dataset.activa !== "true" && aplazadas === 0) detener();
    } catch {
      // Sin conexión con el servidor: se reintenta en la siguiente vuelta.
    }
  }

  // Páginas de la tabla: se espera a que la página pedida esté puesta y el foco va a su título, para seguir leyendo desde ahí.
  document.addEventListener("click", async (evento) => {
    const enlace = evento.target.closest("[data-paginacion] [data-pagina]");
    if (!enlace) return;
    evento.preventDefault();
    pagina = Number(enlace.dataset.pagina);
    history.replaceState(null, "", `?pagina=${pagina}`);
    await refrescar({ forzar: "posiciones" });
    const titulo = document.getElementById("leyenda-posiciones");
    titulo?.setAttribute("tabindex", "-1");
    titulo?.focus();
  });

  function detener() {
    activa = false;
    clearInterval(temporizador);
    conexion?.stop();
  }

  // La antigüedad avanza cada segundo entre actualizaciones («hace 12 s» / «hace 3 min 5 s», como el servidor).
  function antiguedad(desde) {
    const segundos = Math.max(0, Math.floor((Date.now() - new Date(desde).getTime()) / 1000));
    return segundos < 60 ? `hace ${segundos} s` : `hace ${Math.floor(segundos / 60)} min ${segundos % 60} s`;
  }
  const reloj = setInterval(() => {
    if (!activa) { clearInterval(reloj); return; }
    for (const span of document.querySelectorAll("[data-antiguedad]")) span.textContent = antiguedad(span.dataset.desde);
  }, 1000);

  if (!activa) return;
  temporizador = setInterval(() => refrescar(), 5000);
  if (typeof signalR === "undefined") return;
  conexion = new signalR.HubConnectionBuilder().withUrl("/hubs/central").withAutomaticReconnect().build();
  conexion.on("AcompanamientoActualizado", ({ id }) => { if (id === sesion) refrescar(); });
  // El SOS de «Necesito ayuda» puede cancelarse o cerrarse: la confirmación de bienestar lo refleja.
  conexion.on("SosCancelado", () => refrescar());
  conexion.on("CasoActualizado", () => refrescar());
  conexion.onreconnected(() => refrescar()); // lo que llegó durante el corte se lee de la base de datos
  conexion.start().catch(() => { /* sin canal en tiempo real: queda la consulta cada 10 s */ });
})();
