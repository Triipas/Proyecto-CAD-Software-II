// Analítica («Preguntas del analista»): los mapas de zonas (Leaflet), el envío de los filtros al cambiar y «Ahora» en vivo.
// Todo lo demás llega dibujado del servidor: sin JavaScript la página se lee entera (los mapas tienen su tabla para lector).
(() => {
  // ---------- Filtros ----------
  for (const control of document.querySelectorAll("[data-enviar-al-cambiar]"))
    control.addEventListener("change", () => control.form.requestSubmit());

  // ---------- Presentación al cargar ----------
  // Las tarjetas, las barras y los puntos entran con CSS (analitica.css, «Presentación»). Aquí: las cifras del titular, que ruedan
  // dígito a dígito como un tablero de aeropuerto; el orden de filas y columnas (--i) para que crezcan una tras otra; los
  // marcadores de la regla que se deslizan desde 0, y las demás cifras, que cuentan hasta su valor. Todo con la misma salida
  // cúbica: arranca decidida y frena sin arrastrarse (la exponencial corría al principio y se quedaba quieta al final).
  // Solo al cargar la página («Ahora» en vivo cambia sin repetirlo). Con movimiento reducido, nada: las cifras quedan como llegan.
  const CIFRAS = [
    ".pregunta__dato", ".cifras-a dd", ".equipo-a dd", ".etapa-radar__numero", ".radar-a__centro b", ".fila-a__cifra", ".senal-a__valor",
    ".prioridad-a__valor b", ".notas-a__cifra", ".tramo-a__valor", ".fila100__valor", ".categoria-a__valor", ".cadena-a__valor",
    ".barra100__leyenda b", ".etapas-a b", ".regla__valor", ".grupo100__titulo .tarjeta-a__nota", ".leyenda-turno b",
  ].join(", ");
  // Un número suelto: no el de un nombre de zona («K16») ni el de «p90». El que sigue a «de» es el total o la escala («16 de 36»,
  // «3,9 de 5»): queda fijo y solo cuenta el primero.
  const NUMERO = /(?<![\p{L}\d])(?<!\bde\s)\d+(?:[.,]\d+)?(?![\p{L}\d])/gu;
  const FILAS = [
    ".categoria-a", ".prioridad-a", ".fila100", ".senal-a", ".alza-a", ".etapas-radar > li", ".cadena-a__tramo", ".fila-a", "tbody > tr",
    ".notas-a__columna", ".horas-a__casos", ".horas-a__serenos",
  ];
  const DURACION_CIFRAS = 1300;
  const DURACION_TABLERO = 1600;
  const CURVA_CUBICA = "cubic-bezier(0.33, 1, 0.68, 1)"; // la misma salida cúbica, para las animaciones del navegador
  const suave = (t) => 1 - Math.pow(1 - t, 3);
  const retrasos = new Map(); // el retraso de entrada de cada tarjeta, leído una vez
  presentar();

  function presentar() {
    if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    ordenar();
    rodarTitulares();
    deslizarReglas();
    contarCifras();
  }

  // El titular de la respuesta es el tablero: cada número se arma con una cinta de dígitos por cifra (0 a 9 y otra vez 0) que
  // gira hasta su valor. Las unidades ruedan seguido; cada cifra de más arriba avanza cuando la de abajo pasa de 9 a 0, como un
  // cuentakilómetros. Mientras rueda, el lector de pantalla lee el número final; al terminar vuelve el texto tal como llegó.
  function rodarTitulares() {
    const tableros = [];
    for (const em of document.querySelectorAll(".respuesta__titular em")) {
      const numeros = [];
      const nodos = [];
      const caminante = document.createTreeWalker(em, NodeFilter.SHOW_TEXT);
      for (let nodo = caminante.nextNode(); nodo; nodo = caminante.nextNode()) nodos.push(nodo);
      for (const nodo of nodos) {
        const texto = nodo.nodeValue;
        const coincidencias = [...texto.matchAll(NUMERO)];
        if (coincidencias.length === 0) continue;
        const fragmento = document.createDocumentFragment();
        let desde = 0;
        for (const c of coincidencias) {
          fragmento.append(texto.slice(desde, c.index), armarTablero(c[0], numeros));
          desde = c.index + c[0].length;
        }
        fragmento.append(texto.slice(desde));
        nodo.replaceWith(fragmento);
      }
      if (numeros.length) tableros.push({ em, numeros, inicio: retrasoDe(em) + 220, hecho: false });
    }
    if (tableros.length === 0) return;
    for (const t of tableros) girar(t, 0);
    const origen = performance.now();
    requestAnimationFrame(function paso(ahora) {
      let quedan = false;
      for (const t of tableros) {
        if (t.hecho) continue;
        const avance = Math.min(1, Math.max(0, (ahora - origen - t.inicio) / DURACION_TABLERO));
        if (avance < 1) { girar(t, suave(avance)); quedan = true; continue; }
        // Fin: el texto de siempre, en un solo nodo, para que la actualización en vivo y las pruebas lo encuentren igual.
        for (const n of t.numeros) n.caja.replaceWith(document.createTextNode(n.texto));
        t.em.normalize();
        t.hecho = true;
      }
      if (quedan) requestAnimationFrame(paso);
    });
  }

  function armarTablero(texto, numeros) {
    const caja = document.createElement("span");
    const leido = document.createElement("span");
    leido.className = "visualmente-oculto";
    leido.textContent = texto;
    const vista = document.createElement("span");
    vista.className = "tablero__vista";
    vista.setAttribute("aria-hidden", "true");
    const entero = texto.split(/[.,]/)[0];
    const columnas = [];
    [...texto].forEach((caracter, i) => {
      if (!/\d/.test(caracter)) { vista.append(caracter); return; }
      const columna = document.createElement("span");
      columna.className = "tablero__columna";
      const cinta = document.createElement("span");
      cinta.className = "tablero__cinta";
      for (const digito of "01234567890") cinta.append(Object.assign(document.createElement("span"), { textContent: digito }));
      columna.append(cinta);
      vista.append(columna);
      // Lugar de la cifra: 0 las unidades, 1 las decenas…; -1 la primera decimal.
      columnas.push({ cinta, lugar: i < entero.length ? entero.length - 1 - i : entero.length - i });
    });
    caja.append(leido, vista);
    numeros.push({ caja, texto, valor: parseFloat(texto.replace(",", ".")), columnas, minimo: Math.min(...columnas.map((c) => c.lugar)) });
    return caja;
  }

  function girar(tablero, fraccion) {
    for (const n of tablero.numeros) {
      const v = n.valor * fraccion;
      for (const { cinta, lugar } of n.columnas) {
        let posicion;
        if (lugar === n.minimo) {
          posicion = (v / 10 ** lugar) % 10;
        } else {
          const debajo = (v / 10 ** (lugar - 1)) % 10;
          posicion = (Math.floor(v / 10 ** lugar + 1e-9) % 10) + Math.max(0, debajo - 9);
        }
        cinta.style.transform = `translateY(${-posicion}lh)`;
      }
    }
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
    const curva = CURVA_CUBICA; // la misma que la cuenta del rótulo: el número y la posición avanzan juntos
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
        pintar(tarea, avance === 1 ? 1 : suave(avance));
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
    const advertencia = getComputedStyle(document.documentElement).getPropertyValue("--color-advertencia").trim() || "#9A6700";
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

  // ---------- «Ahora»: el radar ----------
  // La entrada (el haz que da una vuelta y descubre los casos) la hace el CSS mientras el radar tiene .radar-a--entrando; aquí se
  // pone al cargar y se quita al terminar, para que en vivo no se repita. Al pasar el cursor o el foco por una etapa de la lista,
  // su porción se ilumina y lo demás se atenúa; un clic la deja fija. La actualización en vivo rehace los atributos de la región:
  // aplicar() vuelve a poner lo que solo sabe el navegador (el foco, la fijada, la entrada que sigue corriendo).
  const radar = (() => {
    const DURACION_ENTRADA = 3200;
    let foco = null, fija = null, entrandoHasta = 0;
    const nodo = () => document.querySelector("[data-radar]");

    function aplicar() {
      const r = nodo();
      if (!r) return;
      r.classList.toggle("radar-a--entrando", performance.now() < entrandoHasta);
      const etapa = foco ?? fija;
      if (etapa) r.dataset.foco = etapa;
      else delete r.dataset.foco;
      for (const el of r.querySelectorAll("[data-etapa]")) el.classList.toggle("es-foco", el.dataset.etapa === etapa);
      for (const boton of document.querySelectorAll(".etapa-radar")) boton.setAttribute("aria-pressed", String(boton.dataset.etapa === fija));
    }

    function enfocar(etapa) {
      if (foco === etapa) return;
      foco = etapa;
      aplicar();
    }

    const botonDe = (evento) => (evento.target instanceof Element ? evento.target.closest(".etapa-radar") : null);
    document.addEventListener("mouseover", (e) => { const b = botonDe(e); if (b) enfocar(b.dataset.etapa); });
    document.addEventListener("mouseout", (e) => { const b = botonDe(e); if (b && !b.contains(e.relatedTarget)) enfocar(null); });
    document.addEventListener("focusin", (e) => { const b = botonDe(e); if (b) enfocar(b.dataset.etapa); });
    document.addEventListener("focusout", (e) => { if (botonDe(e)) enfocar(null); });
    document.addEventListener("click", (e) => {
      const b = botonDe(e);
      if (!b) return;
      fija = fija === b.dataset.etapa ? null : b.dataset.etapa;
      aplicar();
    });

    if (nodo() && !matchMedia("(prefers-reduced-motion: reduce)").matches) {
      entrandoHasta = performance.now() + DURACION_ENTRADA;
      aplicar();
      setTimeout(aplicar, DURACION_ENTRADA + 50);
    }

    // Un caso que acaba de entrar aparece con dos ondas (el CSS no las corre con movimiento reducido).
    function marcarNuevos(claves) {
      const r = nodo();
      if (!r) return;
      for (const clave of claves) {
        if (!clave.startsWith("caso-")) continue;
        r.querySelector(`[data-clave="${CSS.escape(clave)}"]`)?.classList.add("radar-a__caso--nuevo");
      }
    }

    return { aplicar, marcarNuevos };
  })();

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
      // Los puntos del radar tienen data-clave: los que siguen se deslizan a su sitio nuevo (movimiento.js).
      const { nuevos } = cadMovimiento.actualizarRegion(region, nueva.innerHTML, { animar: true, despues: radar.aplicar });
      radar.marcarNuevos(nuevos);
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
