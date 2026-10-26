// Detalle de una sesión de acompañamiento (24/10/2026).
//  - Páginas de posiciones sin recargar: «Más recientes» / «Anteriores» piden la página y cambian solo la tabla. Sin
//    JavaScript son enlaces normales (?pagina=N).
//  - Con la sesión activa, el detalle se actualiza solo: se consulta cada 5 s y, además, cada vez que el canal de la central
//    avisa que llegó algo del ciudadano (posición, confirmación, aviso o cierre). Se vuelve a pedir la misma página y se
//    cambian solo las partes marcadas con data-vivo. Si el foco del operador está en una parte, esta se cambia igual y el foco
//    vuelve al elemento equivalente; solo se deja para después la parte en la que está escribiendo o la que muestra un error
//    recién recibido.
// 26/10/2026: el detalle es un modo de la vigilancia, sobre su mismo mapa. acompanamientos.js lo inicia al entrar al modo
// (CadDetalleAcompanamiento.iniciar) y lo detiene al salir; el canal de la central, la antigüedad al segundo y el mapa son suyos.
(() => {
  function iniciar(raiz, { alActualizar } = {}) {
    const url = `/panel/acompanamientos/${raiz.dataset.sesion}`;
    let pagina = (location.pathname === url && Number(new URLSearchParams(location.search).get("pagina"))) || 1;
    let activa = raiz.dataset.activa === "true";
    let temporizador = null, detenido = false;

    // Una sola petición a la vez; las que llegan mientras tanto se juntan en una sola siguiente, que conserva la parte que
    // debe cambiarse sí o sí (la tabla al cambiar de página). Cada llamada devuelve la promesa de la petición que la atiende.
    let enCurso = null, siguiente = null;
    function refrescar({ forzar = null } = {}) {
      if (detenido) return Promise.resolve();
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
      if (elemento.dataset.verPosicion) return `[data-ver-posicion="${CSS.escape(elemento.dataset.verPosicion)}"]`;
      if (elemento.dataset.pagina) return `[data-pagina="${CSS.escape(elemento.dataset.pagina)}"]`;
      if (elemento.matches("a[href]")) return `a[href="${CSS.escape(elemento.getAttribute("href"))}"]`;
      if (elemento.matches("button[type=submit]")) return "button[type=submit]";
      return null;
    }

    async function ejecutar(forzar) {
      let aplazadas = 0;
      try {
        const respuesta = await fetch(`${url}?pagina=${pagina}`, { credentials: "same-origin", headers: { Accept: "text/html" } });
        if (detenido || respuesta.redirected || !respuesta.ok) return; // sesión del panel terminada o error: se conserva lo mostrado
        const nuevo = new DOMParser().parseFromString(await respuesta.text(), "text/html");
        if (detenido) return;
        for (const region of raiz.querySelectorAll("[data-vivo]")) {
          const reemplazo = nuevo.querySelector(`[data-vivo="${region.dataset.vivo}"]`);
          if (!reemplazo) continue;
          if (region.dataset.vivo !== forzar && aplazar(region)) { aplazadas++; continue; }
          const foco = region.contains(document.activeElement) ? huella(document.activeElement) : null;
          const elegida = region.querySelector(".posicion--elegida [data-ver-posicion]")?.dataset.verPosicion;
          const importada = document.importNode(reemplazo, true);
          region.replaceWith(importada);
          if (foco) importada.querySelector(foco)?.focus({ preventScroll: true });
          // La posición ubicada en el mapa sigue resaltada en su fila.
          if (elegida) importada.querySelector(`[data-ver-posicion="${CSS.escape(elegida)}"]`)?.closest("[data-posicion]")?.classList.add("posicion--elegida");
        }
        alActualizar?.();
        // La sesión terminó: se deja de actualizar solo cuando ya no queda ninguna parte por poner al día.
        if (nuevo.querySelector("[data-sesion]")?.dataset.activa !== "true" && aplazadas === 0) {
          activa = false;
          clearInterval(temporizador);
        }
      } catch {
        // Sin conexión con el servidor: se reintenta en la siguiente vuelta.
      }
    }

    // Páginas de la tabla: se espera a que la página pedida esté puesta y el foco va a su título, para seguir leyendo desde ahí.
    async function paginar(evento) {
      const enlace = evento.target.closest("[data-paginacion] [data-pagina]");
      if (!enlace) return;
      evento.preventDefault();
      pagina = Number(enlace.dataset.pagina);
      history.replaceState(history.state, "", `${url}?pagina=${pagina}`);
      await refrescar({ forzar: "posiciones" });
      const titulo = raiz.querySelector("#leyenda-posiciones");
      titulo?.setAttribute("tabindex", "-1");
      titulo?.focus();
    }
    raiz.addEventListener("click", paginar);

    if (activa) temporizador = setInterval(() => refrescar(), 5000);

    return {
      // El canal de la central avisa algo: solo cuenta mientras la sesión siga activa.
      refrescar: () => (activa ? refrescar() : Promise.resolve()),
      detener() {
        detenido = true;
        clearInterval(temporizador);
        raiz.removeEventListener("click", paginar);
      },
    };
  }

  window.CadDetalleAcompanamiento = { iniciar };
})();
