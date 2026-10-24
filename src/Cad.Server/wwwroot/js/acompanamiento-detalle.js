// Detalle de una sesión de acompañamiento (24/10/2026).
//  - Páginas de posiciones sin recargar: «Más recientes» / «Anteriores» piden la página y cambian solo la tabla. Sin
//    JavaScript son enlaces normales (?pagina=N).
//  - Con la sesión activa, la página se actualiza sola: el servidor avisa por el canal de la central cada vez que llega algo
//    del ciudadano (posición, confirmación, aviso o cierre) y, por si se pierde un aviso o pasa el umbral de «sin conexión»,
//    también se consulta cada 10 s. Se vuelve a pedir la misma página y se cambian solo las partes marcadas con data-vivo;
//    la que tiene el foco del operador (o un error recién mostrado) no se toca para no interrumpirle.
(() => {
  const raiz = document.querySelector("[data-sesion]");
  if (!raiz) return;
  const sesion = raiz.dataset.sesion;
  let pagina = Number(new URLSearchParams(location.search).get("pagina")) || 1;
  let activa = raiz.dataset.activa === "true";
  let pidiendo = false, pendiente = false, temporizador = null, conexion = null;

  function enUso(region) {
    const foco = document.activeElement;
    return (foco && foco !== document.body && region.contains(foco)) || region.querySelector("[data-error-confirmacion]") !== null;
  }

  async function refrescar({ forzar = null } = {}) {
    if (pidiendo) { pendiente = true; return; }
    pidiendo = true;
    try {
      const respuesta = await fetch(`${location.pathname}?pagina=${pagina}`, { credentials: "same-origin", headers: { Accept: "text/html" } });
      if (respuesta.redirected || !respuesta.ok) return; // sesión del panel terminada o error: se conserva lo mostrado
      const nuevo = new DOMParser().parseFromString(await respuesta.text(), "text/html");
      for (const region of document.querySelectorAll("[data-vivo]")) {
        const reemplazo = nuevo.querySelector(`[data-vivo="${region.dataset.vivo}"]`);
        if (!reemplazo || (region.dataset.vivo !== forzar && enUso(region))) continue;
        region.replaceWith(document.importNode(reemplazo, true));
      }
      const mapa = nuevo.getElementById("mapa-recorrido");
      if (mapa) window.CadRecorrido?.actualizar(mapa.dataset.puntos, mapa.dataset.actual === "true");
      if (nuevo.querySelector("[data-sesion]")?.dataset.activa !== "true") detener(); // la sesión terminó
    } catch {
      // Sin conexión con el servidor: se reintenta en la siguiente vuelta.
    } finally {
      pidiendo = false;
      if (pendiente) { pendiente = false; refrescar(); }
    }
  }

  // Páginas de la tabla: se cambia la tabla y el foco va a su título, para seguir leyendo desde ahí.
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

  if (!activa) return;
  temporizador = setInterval(refrescar, 10000);
  if (typeof signalR === "undefined") return;
  conexion = new signalR.HubConnectionBuilder().withUrl("/hubs/central").withAutomaticReconnect().build();
  conexion.on("AcompanamientoActualizado", ({ id }) => { if (id === sesion) refrescar(); });
  conexion.onreconnected(() => refrescar()); // lo que llegó durante el corte se lee de la base de datos
  conexion.start().catch(() => { /* sin canal en tiempo real: queda la consulta cada 10 s */ });
})();
