// Consola de incidente (27/10/2026). Dos tareas:
// 1. «hace N min» de la barra del caso avanza solo, y el cronómetro sube de tono con el tiempo (data-nivel: 0 normal, 1 a los
//    2 min, 2 a los 5 min) mientras el caso espera a la central.
// 2. El detalle es en vivo (DESIGN.md: «lo que cambia se mueve en su sitio»): escucha el canal de la central y, si este caso
//    cambia, se pone al día. Sin nada a medio escribir, la página se vuelve a cargar en el mismo punto; con un formulario a
//    medio llenar no se toca lo que se escribe: se actualizan la barra, los pasos y el expediente, y un aviso ofrece ver los
//    cambios. Un SOS nuevo en otro caso se anuncia en la barra con un enlace a la central.
(() => {
  // ── Cronómetro ──────────────────────────────────────────
  const cronometros = document.querySelectorAll("[data-transcurrido]");

  const texto = (ms) => {
    const minutos = Math.floor(ms / 60000);
    if (minutos < 1) return "hace menos de 1 min";
    if (minutos < 60) return `hace ${minutos} min`;
    const horas = Math.floor(minutos / 60);
    if (horas < 24) return `hace ${horas} h ${minutos % 60} min`;
    return `hace ${Math.floor(horas / 24)} d ${horas % 24} h`;
  };

  const actualizarCronometros = () => {
    const ahora = Date.now();
    for (const elemento of cronometros) {
      const desde = Date.parse(elemento.dataset.transcurrido);
      if (Number.isNaN(desde)) continue;
      const salida = elemento.querySelector("[data-texto]") ?? elemento;
      salida.textContent = texto(ahora - desde);
      if (elemento.hasAttribute("data-escala")) {
        const minutos = (ahora - desde) / 60000;
        elemento.dataset.nivel = minutos >= 5 ? "2" : minutos >= 2 ? "1" : "0";
      }
    }
  };

  if (cronometros.length > 0) {
    actualizarCronometros();
    setInterval(actualizarCronometros, 15000);
  }

  // «Despachar ahora»: abre «Otras acciones» en la sección de despacho y lleva el foco al primer sereno.
  for (const boton of document.querySelectorAll("[data-ir-a]")) {
    boton.addEventListener("click", () => {
      const titulo = document.getElementById(boton.dataset.irA);
      if (!titulo) return;
      const plegable = titulo.closest("details");
      if (plegable) plegable.open = true;
      const seccion = titulo.closest("section") ?? titulo;
      seccion.scrollIntoView({ block: "start", behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth" });
      seccion.querySelector("input:not([type=hidden]), select, button")?.focus({ preventScroll: true });
    });
  }

  for (const boton of document.querySelectorAll("[data-recargar]"))
    boton.addEventListener("click", () => location.reload());

  // ── En vivo ─────────────────────────────────────────────
  const barra = document.querySelector("[data-caso]");
  if (!barra || typeof signalR === "undefined") return;
  const casoId = barra.dataset.caso;
  const vivo = document.querySelector("[data-vivo]");
  const anuncio = document.querySelector("[data-anuncio]");

  const estadoVivo = (estado, textoEstado) => {
    if (!vivo) return;
    vivo.dataset.estado = estado;
    vivo.querySelector("[data-texto]").textContent = textoEstado;
  };

  const anunciar = (mensaje) => {
    if (anuncio) anuncio.textContent = mensaje;
  };

  // ¿Hay algo escrito o elegido que se perdería al recargar?
  const formularioAMedias = () =>
    [...document.querySelectorAll(".consola__trabajo form")].some((formulario) =>
      [...formulario.elements].some((campo) => {
        if (campo.type === "hidden" || campo.type === "submit" || campo.disabled) return false;
        if (campo.type === "radio" || campo.type === "checkbox") return campo.checked !== campo.defaultChecked;
        // Sin una opción marcada en el HTML, el navegador muestra la primera: eso no es un cambio del operador.
        if (campo.tagName === "SELECT") {
          const porDefecto = Math.max(0, [...campo.options].findIndex((o) => o.defaultSelected));
          return campo.selectedIndex !== porDefecto;
        }
        return "value" in campo && campo.value !== campo.defaultValue;
      }));

  async function ponerAlDia({ cancelado = false } = {}) {
    if (!formularioAMedias()) {
      sessionStorage.setItem("consola-desplazamiento", String(window.scrollY));
      location.reload();
      return;
    }
    let documento;
    try {
      const respuesta = await fetch(location.pathname, { headers: { "X-Requested-With": "fetch" }, credentials: "same-origin" });
      if (!respuesta.ok) return;
      documento = new DOMParser().parseFromString(await respuesta.text(), "text/html");
    } catch {
      return;
    }
    const estadoAntes = document.querySelector(".consola__barra [data-estado]")?.textContent.trim();
    // Lo que no tiene controles ni mapa se actualiza en su sitio.
    for (const selector of [".consola__barra .consola__ancho > :not(.consola__acciones)", ".consola__pasos", "[aria-labelledby='titulo-clasificacion']", "[data-tiempos]"]) {
      const actuales = document.querySelectorAll(selector);
      const nuevos = documento.querySelectorAll(selector);
      actuales.forEach((actual, i) => {
        if (!nuevos[i]) return;
        if (window.cadMovimiento?.actualizarRegion) window.cadMovimiento.actualizarRegion(actual, nuevos[i].innerHTML, { animar: true });
        else actual.innerHTML = nuevos[i].innerHTML;
      });
    }
    const estado = documento.querySelector(".consola__barra [data-estado]")?.textContent.trim();
    const aviso = document.querySelector("[data-caso-cambio]");
    if (aviso) {
      aviso.querySelector("[data-texto]").textContent = cancelado
        ? "El ciudadano canceló este SOS mientras escribías. Lo que escribiste no se registró."
        : estado && estado !== estadoAntes
          ? `El caso pasó a «${estado}» mientras escribías. Revisa los cambios antes de registrar.`
          : "Otro operador registró una novedad en este caso mientras escribías. Revisa los cambios antes de registrar.";
      aviso.dataset.tono = cancelado ? "urgente" : "informacion";
      aviso.hidden = false;
    }
    anunciar(cancelado ? "El ciudadano canceló este SOS." : estado && estado !== estadoAntes ? `El caso pasó a ${estado}.` : "Hay una novedad en este caso.");
  }

  // Al volver de una recarga automática, se conserva el lugar.
  const desplazamiento = sessionStorage.getItem("consola-desplazamiento");
  if (desplazamiento !== null) {
    sessionStorage.removeItem("consola-desplazamiento");
    window.scrollTo({ top: Number(desplazamiento), behavior: "instant" });
  }

  function nuevoSos(id) {
    if (id === casoId) return;
    const aviso = document.querySelector("[data-otro-sos]");
    if (!aviso) return;
    aviso.hidden = false;
    anunciar("Entró un SOS nuevo en la central.");
    window.CadSonidos?.reproducir("sos-nuevo");
  }

  const conexion = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/central")
    .withAutomaticReconnect([0, 2000, 5000, 10000, 15000])
    .build();

  conexion.on("CasoActualizado", ({ id }) => { if (id === casoId) ponerAlDia(); });
  conexion.on("SosCancelado", ({ id }) => { if (id === casoId) ponerAlDia({ cancelado: true }); });
  conexion.on("SosRecibido", ({ id }) => nuevoSos(id));
  conexion.onreconnecting(() => estadoVivo("reconectando", "Reconectando…"));
  conexion.onreconnected(() => { estadoVivo("conectado", "En vivo"); ponerAlDia(); });
  conexion.onclose(() => { estadoVivo("desconectado", "Sin conexión en vivo"); setTimeout(iniciar, 5000); });

  async function iniciar() {
    try {
      await conexion.start();
      estadoVivo("conectado", "En vivo");
    } catch {
      estadoVivo("desconectado", "Sin conexión en vivo");
      setTimeout(iniciar, 5000);
    }
  }

  iniciar();
})();
