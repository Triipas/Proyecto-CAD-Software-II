// Analítica («Preguntas del analista»): los mapas de zonas (Leaflet), el envío de los filtros al cambiar y «Ahora» en vivo.
// Todo lo demás llega dibujado del servidor: sin JavaScript la página se lee entera (los mapas tienen su tabla para lector).
(() => {
  // ---------- Filtros ----------
  for (const control of document.querySelectorAll("[data-enviar-al-cambiar]"))
    control.addEventListener("change", () => control.form.requestSubmit());

  // ---------- Mapas de zonas ----------
  const contenedor = document.getElementById("mapa-zonas");
  if (contenedor && typeof L !== "undefined") dibujarMapa(contenedor);

  function leer(selector) {
    const nodo = contenedor.querySelector(selector);
    return nodo ? JSON.parse(nodo.textContent) : [];
  }

  function dibujarMapa(contenedor) {
    L.Control.Attribution.prototype.options.prefix = false;
    const mapa = L.map(contenedor, { zoomControl: false, scrollWheelZoom: false });
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

    // Una zona: celda coloreada en la rampa (un solo tono, de claro a oscuro) y su nombre encima; el valor va en el título.
    function zona(z, proporcion, texto, { alerta = false } = {}) {
      const color = rampa[Math.max(0, Math.min(rampa.length - 1, Math.round(proporcion * (rampa.length - 1))))];
      const caja = [[z.sur, z.oeste], [z.norte, z.este]];
      L.rectangle(caja, {
        color: alerta ? advertencia : "#fff",
        weight: alerta ? 2 : 1,
        dashArray: alerta ? "5 4" : null,
        fillColor: color,
        fillOpacity: 0.72,
      }).bindTooltip(texto, { direction: "top" }).addTo(mapa);
      L.marker([(z.sur + z.norte) / 2, (z.oeste + z.este) / 2], {
        icon: L.divIcon({ className: "zona-a", html: z.nombre.replace("Zona ", ""), iconSize: [40, 16] }),
        interactive: false,
        keyboard: false,
      }).addTo(mapa);
      limites.push(...caja);
    }

    if (escala === "minutos" || escala === "cobertura") {
      const zonas = leer("[data-zonas]");
      const maximo = Math.max(1, ...zonas.map((z) => z.valor));
      for (const z of zonas) {
        if (escala === "minutos")
          zona(z, z.valor / maximo, `${z.nombre}: p90 ${minutos(z.valor)} · ${z.casos} casos`);
        else
          zona(z, z.valor / 100, `${z.nombre}: ${z.valor} % del tiempo con un sereno cerca · ${z.casos} casos`, { alerta: z.alerta });
      }
      for (const s of leer("[data-serenos]")) {
        L.marker([s.lat, s.lon], {
          icon: L.divIcon({ className: `sereno-a${s.disponible ? "" : " sereno-a--ocupado"}`, iconSize: [14, 14] }),
          title: s.disponible ? "Sereno disponible" : "Sereno con caso",
          keyboard: false,
        }).addTo(mapa);
        limites.push([s.lat, s.lon]);
      }
    } else if (escala === "repite") {
      for (const a of leer("[data-alzas]")) {
        L.rectangle([[a.sur, a.oeste], [a.norte, a.este]], {
          color: a.alerta ? advertencia : rampa[4],
          weight: 2,
          dashArray: "5 4",
          fillColor: a.alerta ? advertencia : rampa[2],
          fillOpacity: 0.12,
        }).bindTooltip(`${a.nombre}: ${a.texto}`, { direction: "top" }).addTo(mapa);
        limites.push([a.sur, a.oeste], [a.norte, a.este]);
      }
      for (const p of leer("[data-puntos]")) {
        const lado = Math.round(26 + Math.min(18, p.casos * 2));
        const texto = `Punto ${p.numero}: ${p.casos} casos${p.categoria ? ` · sobre todo ${p.categoria.toLowerCase()}` : ""}`;
        L.circle([p.lat, p.lon], { radius: 100, color: rampa[5], weight: 1, fillColor: rampa[4], fillOpacity: 0.18, interactive: false }).addTo(mapa);
        L.marker([p.lat, p.lon], {
          icon: L.divIcon({ className: "punto-a", html: String(p.casos), iconSize: [lado, lado] }),
          title: texto,
          alt: texto,
        }).addTo(mapa);
        limites.push([p.lat, p.lon]);
      }
    }

    if (limites.length) mapa.fitBounds(limites, { padding: [36, 36], maxZoom: 16 });
    else mapa.setView([-16.3988, -71.5369], 13);
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
  for (const evento of ["CasoActualizado", "SosRecibido", "SerenoActualizado"]) conexion.on(evento, pedir);
  conexion.onreconnecting(() => estado("desconectado"));
  conexion.onreconnected(() => { estado("conectado"); actualizar(); });
  conexion.onclose(() => estado("desconectado"));
  conexion.start().then(() => estado("conectado")).catch(() => estado("desconectado"));

  window.cadAnalitica = { actualizar };
})();
