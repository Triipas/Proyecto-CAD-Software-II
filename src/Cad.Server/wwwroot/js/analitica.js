// Analítica («Preguntas del analista»): los mapas de zonas (Leaflet), el envío de los filtros al cambiar y «Ahora» en vivo.
// Todo lo demás llega dibujado del servidor: sin JavaScript la página se lee entera (los mapas tienen su tabla para lector).
(() => {
  // ---------- Filtros ----------
  for (const control of document.querySelectorAll("[data-enviar-al-cambiar]"))
    control.addEventListener("change", () => control.form.requestSubmit());

  // ---------- Presentación al cargar ----------
  // Las tarjetas, las barras y los puntos entran con CSS (analitica.css, «Presentación»). Aquí: el orden de filas y columnas (--i)
  // para que crezcan una tras otra, los marcadores de la regla que se deslizan desde 0 y las cifras que cuentan hasta su valor.
  // Solo al cargar la página («Ahora» en vivo cambia sin repetirlo). Con movimiento reducido, nada: las cifras quedan como llegan.
  const CIFRAS = [
    ".pregunta__dato", ".respuesta__titular em", ".cifras-a dd", ".equipo-a dd", ".carril-a__numero", ".fila-a__cifra", ".senal-a__valor",
    ".prioridad-a__valor b", ".notas-a__cifra", ".tramo-a__valor", ".fila100__valor", ".categoria-a__valor", ".cadena-a__valor",
    ".barra100__leyenda b", ".etapas-a b", ".regla__valor", ".grupo100__titulo .tarjeta-a__nota", ".serenos-a + .leyenda-a li",
  ].join(", ");
  // Un número suelto: no el de un nombre de zona («K16») ni el de «p90». El que sigue a «de» es el total o la escala («16 de 36»,
  // «3,9 de 5»): queda fijo y solo cuenta el primero.
  const NUMERO = /(?<![\p{L}\d])(?<!\bde\s)\d+(?:[.,]\d+)?(?![\p{L}\d])/gu;
  const FILAS = [
    ".categoria-a", ".prioridad-a", ".fila100", ".senal-a", ".alza-a", ".carril-a", ".cadena-a__tramo", ".fila-a", "tbody > tr",
    ".notas-a__columna", ".horas-a__casos", ".horas-a__serenos",
  ];
  const DURACION_CIFRAS = 1000;
  const retrasos = new Map(); // el retraso de entrada de cada tarjeta, leído una vez
  presentar();

  function presentar() {
    if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    ordenar();
    deslizarReglas();
    contarCifras();
  }

  // El retraso con que entra el bloque de un elemento (su tarjeta), para que lo de dentro empiece con ella.
  function retrasoDe(elemento) {
    const bloque = elemento.closest(".flotante-a, .tarjeta-a");
    if (!bloque) return 0;
    if (!retrasos.has(bloque)) {
      const valor = getComputedStyle(bloque).animationDelay.split(",")[0].trim();
      retrasos.set(bloque, valor.endsWith("ms") ? parseFloat(valor) : (parseFloat(valor) || 0) * 1000);
    }
    return retrasos.get(bloque);
  }

  // Filas y columnas de un mismo gráfico: --i es su orden entre hermanas (hasta 12, para que la cascada no se alargue).
  function ordenar() {
    for (const selector of FILAS) {
      const vistas = new Map();
      for (const el of document.querySelectorAll(`.analitica__trabajo ${selector}`)) {
        const n = vistas.get(el.parentElement) ?? 0;
        vistas.set(el.parentElement, n + 1);
        el.style.setProperty("--i", Math.min(n, 12));
      }
    }
  }

  // La regla del servicio: cada marcador (línea, punto y rótulo) sale de 0 y se desliza hasta su valor mientras el rótulo cuenta.
  // Los rótulos aparecen a mitad de camino: al salir, todos los marcadores están juntos en 0 y sus textos se pisarían.
  function deslizarReglas() {
    const curva = window.cadMovimiento?.curva("salida") ?? "cubic-bezier(0.16, 1, 0.3, 1)";
    for (const regla of document.querySelectorAll("svg.regla")) {
      const retraso = retrasoDe(regla) + 240;
      let x = 0;
      for (const el of regla.querySelectorAll(".regla__linea, .regla__marcador, .regla__valor, .regla__sub")) {
        if (el.matches(".regla__linea")) x = parseFloat(el.getAttribute("x1")) || 0;
        if (x <= 0) continue;
        el.animate([{ transform: `translateX(${-x}px)` }, { transform: "none" }], { duration: DURACION_CIFRAS, delay: retraso, easing: curva, fill: "backwards" });
        if (el.matches(".regla__valor, .regla__sub"))
          el.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 320, delay: retraso + 260, easing: curva, fill: "backwards" });
      }
    }
  }

  // Las cifras cuentan desde 0 con la misma curva que el resto (salida exponencial). Se cuentan los números de los nodos de texto,
  // conservando sus decimales, su coma y sus ceros a la izquierda («1 h 05 min»); mientras cuenta, el número ocupa el ancho del final
  // (espacios de cifra), para que el titular no se reacomode.
  function contarCifras() {
    const tareas = [];
    for (const el of document.querySelectorAll(CIFRAS)) {
      const partes = [];
      const caminante = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
      for (let nodo = caminante.nextNode(); nodo; nodo = caminante.nextNode()) {
        const numeros = [...nodo.nodeValue.matchAll(NUMERO)].map(leerNumero);
        if (numeros.length) partes.push({ nodo, texto: nodo.nodeValue, numeros, pintado: null, hecha: false });
      }
      if (partes.length) tareas.push({ partes, inicio: retrasoDe(el) + 160 });
    }
    if (tareas.length === 0) return;
    for (const tarea of tareas) pintar(tarea, 0);
    const origen = performance.now();
    requestAnimationFrame(function paso(ahora) {
      let quedan = false;
      for (const tarea of tareas) {
        const avance = Math.min(1, Math.max(0, (ahora - origen - tarea.inicio) / DURACION_CIFRAS));
        pintar(tarea, avance === 1 ? 1 : 1 - Math.pow(2, -10 * avance));
        if (avance < 1) quedan = true;
      }
      if (quedan) requestAnimationFrame(paso);
    });
  }

  function leerNumero(coincidencia) {
    const texto = coincidencia[0];
    const [entero, decimales = ""] = texto.split(/[.,]/);
    return {
      desde: coincidencia.index, largo: texto.length, valor: parseFloat(texto.replace(",", ".")), decimales: decimales.length,
      separador: texto.includes(",") ? "," : ".", ancho: entero.length, ceros: entero.length > 1 && entero.startsWith("0"),
    };
  }

  // Escribe cada número a una fracción de su valor; en 1, el texto exacto que llegó del servidor. Si otro cambió el texto mientras
  // tanto (la actualización en vivo), manda ese texto y la cuenta de ese nodo se abandona.
  function pintar(tarea, fraccion) {
    for (const parte of tarea.partes) {
      if (parte.hecha) continue;
      if (parte.pintado !== null && parte.nodo.nodeValue !== parte.pintado) { parte.hecha = true; continue; }
      let texto = parte.texto;
      if (fraccion < 1) {
        for (const n of [...parte.numeros].reverse()) {
          const valor = n.valor * fraccion;
          const [entero, decimales] = (n.decimales ? valor.toFixed(n.decimales) : String(Math.round(valor))).split(".");
          const cifra = entero.padStart(n.ancho, n.ceros ? "0" : " ") + (decimales ? n.separador + decimales : "");
          texto = texto.slice(0, n.desde) + cifra + texto.slice(n.desde + n.largo);
        }
      } else {
        parte.hecha = true;
      }
      parte.nodo.nodeValue = texto;
      parte.pintado = texto;
    }
  }

  // ---------- Mapas de zonas ----------
  const contenedor = document.getElementById("mapa-zonas");
  if (contenedor && typeof L !== "undefined") dibujarMapa(contenedor);

  function leer(selector) {
    const nodo = contenedor.querySelector(selector);
    return nodo ? JSON.parse(nodo.textContent) : [];
  }

  function dibujarMapa(contenedor) {
    L.Control.Attribution.prototype.options.prefix = false;
    // El mapa que ocupa la pantalla (cobertura, lo que se repite) se acerca con la rueda; el de una tarjeta deja pasar la rueda al scroll.
    const mapa = L.map(contenedor, { zoomControl: false, scrollWheelZoom: !!contenedor.closest(".mapa-protagonista") });
    L.tileLayer(contenedor.dataset.teselas, {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
    }).addTo(mapa);
    L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);

    const estilo = getComputedStyle(contenedor.closest(".analitica"));
    const rampa = [1, 2, 3, 4, 5, 6, 7].map((i) => estilo.getPropertyValue(`--rampa-${i}`).trim());
    const advertencia = getComputedStyle(document.documentElement).getPropertyValue("--color-advertencia").trim() || "#93370D";
    const limites = [];
    const escala = contenedor.dataset.escala;

    // Una zona: celda separada de sus vecinas, en un tono de la rampa (más oscuro = más preocupante) y una etiqueta con su nombre y su
    // valor; el detalle completo va en el globo al pasar el cursor.
    function zona(z, proporcion, texto, { alerta = false, valor = "", etiqueta = true } = {}) {
      const color = rampa[Math.max(0, Math.min(rampa.length - 1, Math.round(proporcion * (rampa.length - 1))))];
      const hueco = 0.07; // la celda se encoge un poco para que se vea la cuadrícula
      const dLat = (z.norte - z.sur) * hueco, dLon = (z.este - z.oeste) * hueco;
      const celda = L.rectangle([[z.sur + dLat, z.oeste + dLon], [z.norte - dLat, z.este - dLon]], {
        color: alerta ? advertencia : rampa[6],
        weight: alerta ? 2.5 : 1,
        dashArray: alerta ? "6 4" : null,
        opacity: alerta ? 1 : 0.55,
        fillColor: color,
        fillOpacity: 0.86,
        className: "zona-celda",
      }).bindTooltip(texto, { direction: "top", className: "zona-globo", sticky: true }).addTo(mapa);
      celda.on("mouseover", () => celda.setStyle({ fillOpacity: 0.96, weight: alerta ? 3 : 2, opacity: 1 }));
      celda.on("mouseout", () => celda.setStyle({ fillOpacity: 0.86, weight: alerta ? 2.5 : 1, opacity: alerta ? 1 : 0.55 }));
      limites.push([z.sur, z.oeste], [z.norte, z.este]);
      if (!etiqueta) return;
      const oscura = proporcion > 0.45;
      L.marker([(z.sur + z.norte) / 2, (z.oeste + z.este) / 2], {
        icon: L.divIcon({
          className: "zona-a",
          html: `<span class="zona-a__etiqueta${oscura ? " zona-a__etiqueta--oscura" : ""}${alerta ? " zona-a__etiqueta--alerta" : ""}"><b>${z.nombre.replace("Zona ", "")}</b>${valor ? `<i>${alerta ? "▲ " : ""}${valor}</i>` : ""}</span>`,
          iconSize: [56, 34],
        }),
        interactive: false,
        keyboard: false,
      }).addTo(mapa);
    }

    if (escala === "minutos" || escala === "cobertura") {
      const zonas = leer("[data-zonas]");
      const maximo = Math.max(1, ...zonas.map((z) => z.valor));
      for (const z of zonas) {
        if (escala === "minutos")
          zona(z, z.valor / maximo, `${z.nombre}: p90 ${minutos(z.valor)} · ${z.casos} casos`, { valor: minutos(z.valor) });
        else
          zona(z, 1 - z.valor / 100, `${z.nombre}: ${z.valor} % del tiempo con un sereno libre cerca · ${z.casos} casos`, { alerta: z.alerta, valor: `${z.valor} %` });
      }
      for (const s of leer("[data-serenos]")) {
        L.marker([s.lat, s.lon], {
          icon: L.divIcon({ className: `sereno-a${s.disponible ? "" : " sereno-a--ocupado"}`, iconSize: [14, 14] }),
          title: s.disponible ? "Sereno disponible" : "Sereno con caso",
          keyboard: false,
        }).addTo(mapa);
        limites.push([s.lat, s.lon]);
      }
    } else if (escala === "frecuencia") {
      // «Rendimiento de los serenos»: cuánto de su turno pasó en cada zona (más oscuro = más tiempo) y los casos que atendió ahí. Las
      // zonas de un sereno son vecinas: solo las 6 donde más está llevan etiqueta (todas juntas se pisan); las demás, su globo.
      const zonas = leer("[data-zonas]");
      const maximo = Math.max(1, ...zonas.map((z) => z.valor));
      const rotuladas = new Set([...zonas].sort((a, b) => b.valor - a.valor).slice(0, 6));
      for (const z of zonas)
        zona(z, z.valor / maximo, `${z.nombre}: ${z.horas} (${z.valor} % de su turno) · ${z.casos} ${z.casos === 1 ? "caso atendido" : "casos atendidos"}`,
          { valor: `${z.valor} %`, etiqueta: rotuladas.has(z) });
    } else if (escala === "repite") {
      // Zonas en alza: las que tienen alerta, con su etiqueta; las demás, apenas un trazo punteado.
      for (const a of leer("[data-alzas]")) {
        const hueco = 0.07, dLat = (a.norte - a.sur) * hueco, dLon = (a.este - a.oeste) * hueco;
        L.rectangle([[a.sur + dLat, a.oeste + dLon], [a.norte - dLat, a.este - dLon]], {
          color: a.alerta ? advertencia : rampa[4],
          weight: a.alerta ? 2.5 : 1.5,
          dashArray: a.alerta ? "6 4" : "2 5",
          opacity: a.alerta ? 1 : 0.7,
          fillColor: a.alerta ? advertencia : rampa[2],
          fillOpacity: a.alerta ? 0.18 : 0.05,
          className: "zona-celda",
        }).bindTooltip(`${a.nombre}: ${a.texto}`, { direction: "top", className: "zona-globo", sticky: true }).addTo(mapa);
        if (a.alerta)
          L.marker([a.norte - dLat, (a.oeste + a.este) / 2], {
            icon: L.divIcon({
              className: "zona-a",
              html: `<span class="zona-a__etiqueta zona-a__etiqueta--alerta"><b>${a.nombre.replace("Zona ", "")}</b><i>▲ ${a.texto}</i></span>`,
              iconSize: [96, 34],
              iconAnchor: [48, 38], // sobre el borde norte de la zona, sin tapar los puntos de dentro
            }),
            interactive: false,
            keyboard: false,
          }).addTo(mapa);
        limites.push([a.sur, a.oeste], [a.norte, a.este]);
      }
      // Puntos críticos: el número es el de la lista de la derecha; el tamaño crece con los casos.
      for (const p of leer("[data-puntos]")) {
        const lado = Math.round(28 + Math.min(14, p.casos * 2));
        const texto = `Punto ${p.numero}: ${p.casos} casos${p.categoria ? ` · sobre todo ${p.categoria.toLowerCase()}` : ""}`;
        L.circle([p.lat, p.lon], { radius: 100, color: rampa[5], weight: 1, fillColor: rampa[4], fillOpacity: 0.18, interactive: false }).addTo(mapa);
        L.marker([p.lat, p.lon], {
          icon: L.divIcon({ className: "punto-a", html: String(p.numero), iconSize: [lado, lado] }),
          title: texto,
          alt: texto,
        }).addTo(mapa);
        limites.push([p.lat, p.lon]);
      }
    }

    if (limites.length) mapa.fitBounds(limites, { padding: [36, 36], maxZoom: 16 });
    else mapa.setView([-16.3988, -71.5369], 13);
    // La respuesta flotante se puede plegar para ver todo el mapa.
    const flotanteCaja = contenedor.parentElement.querySelector(".flotante-a");
    if (flotanteCaja) {
      const boton = document.createElement("button");
      boton.type = "button";
      boton.className = "flotante-a__plegar";
      boton.setAttribute("aria-expanded", "true");
      boton.setAttribute("aria-label", "Ocultar la respuesta");
      boton.title = "Ocultar la respuesta";
      boton.innerHTML = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 15 6-6 6 6"/></svg>';
      boton.addEventListener("click", () => {
        const plegada = flotanteCaja.classList.toggle("flotante-a--plegada");
        boton.setAttribute("aria-expanded", String(!plegada));
        const texto = plegada ? "Mostrar la respuesta" : "Ocultar la respuesta";
        boton.setAttribute("aria-label", texto);
        boton.title = texto;
      });
      flotanteCaja.append(boton);
    }
    // La respuesta flotante tapa la esquina del mapa: se corre el encuadre para que las zonas no queden debajo.
    const flotante = contenedor.parentElement.querySelector(".flotante-a");
    if (flotante && limites.length)
      mapa.fitBounds(limites, { paddingTopLeft: [Math.min(flotante.offsetWidth + 24, contenedor.offsetWidth / 2), 36], paddingBottomRight: [36, 36], maxZoom: 16 });
  }

  function minutos(valor) {
    if (valor < 1) return `${Math.round(valor * 60)} s`;
    if (valor < 60) return `${Math.round(valor)} min`;
    const h = Math.floor(valor / 60), m = Math.round(valor % 60);
    return `${h} h ${String(m).padStart(2, "0")} min`;
  }

  // ---------- «Ahora» en vivo ----------
  const vivo = document.querySelector("[data-vivo]");
  let region = document.querySelector("[data-region-ahora]");
  if (!vivo || !region) return;
  const texto = vivo.querySelector("[data-texto]");

  function estado(conexion) {
    vivo.dataset.conexion = conexion;
    const hora = region.dataset.actualizado;
    if (conexion === "conectado") texto.textContent = `En vivo · actualizado ${hora}`;
    else if (conexion === "desconectado") texto.textContent = `Sin conexión en vivo · datos de las ${hora}`;
    else texto.textContent = "Conectando…";
    region.classList.toggle("region-ahora--vieja", conexion === "desconectado");
  }

  let pidiendo = false, pendiente = false, espera = null;
  async function actualizar() {
    if (pidiendo) { pendiente = true; return; }
    pidiendo = true;
    try {
      const respuesta = await fetch(location.href, { credentials: "same-origin", headers: { Accept: "text/html" } });
      if (respuesta.redirected || !respuesta.ok) return; // sesión terminada o error: se conserva lo mostrado
      const pagina = new DOMParser().parseFromString(await respuesta.text(), "text/html");
      const nueva = pagina.querySelector("[data-region-ahora]");
      if (!nueva) return;
      cadMovimiento.actualizarRegion(region, nueva.innerHTML, { animar: true });
      region.dataset.actualizado = nueva.dataset.actualizado;
      // El dato de cada pregunta en la columna también cambia («4 atascados»).
      for (const dato of pagina.querySelectorAll(".pregunta__dato")) {
        const actual = document.querySelector(`[data-pregunta="${dato.parentElement.dataset.pregunta}"] .pregunta__dato`);
        if (actual) { actual.className = dato.className; actual.textContent = dato.textContent; }
      }
      if (vivo.dataset.conexion === "conectado") estado("conectado");
    } catch {
      // Sin conexión con el servidor: se reintenta con el siguiente evento o la siguiente vuelta.
    } finally {
      pidiendo = false;
      if (pendiente) { pendiente = false; actualizar(); }
    }
  }

  // Varios eventos seguidos (un despacho mueve el caso y el sereno) piden una sola actualización.
  function pedir() {
    clearTimeout(espera);
    espera = setTimeout(actualizar, 400);
  }

  // Los minutos en etapa avanzan aunque no llegue ningún evento.
  setInterval(actualizar, 60000);

  if (typeof signalR === "undefined") { estado("desconectado"); return; }
  const conexion = new signalR.HubConnectionBuilder().withUrl("/hubs/central").withAutomaticReconnect().build();
  // Un SOS cancelado y los cambios de un acompañamiento (o un aviso nuevo) también cambian lo que muestra «Ahora».
  for (const evento of ["CasoActualizado", "SosRecibido", "SosCancelado", "SerenoActualizado", "AcompanamientoActualizado"])
    conexion.on(evento, pedir);
  conexion.onreconnecting(() => estado("desconectado"));
  conexion.onreconnected(() => { estado("conectado"); actualizar(); });
  conexion.onclose(() => estado("desconectado"));
  conexion.start().then(() => estado("conectado")).catch(() => estado("desconectado"));

  window.cadAnalitica = { actualizar };
})();
