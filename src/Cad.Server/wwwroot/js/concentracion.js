// HU29 — mapa de concentración como mapa de calor infrarrojo. Dos capas que se apagan por separado:
// - Calor: cada punto (celda de unos 110 m con sus casos) suma un halo; donde se acumulan, la ciudad «arde» de índigo a amarillo
//   casi blanco, como en una cámara térmica. El rojo no entra en la escala: en el panel es solo del SOS.
// - Cifras por zona: una insignia en cada zona de unos 550 m con 3 casos o más (las 20 con más casos); lo demás lo dice el calor. SOS en cuadro con borde rojo y reportes en círculo con
//   borde violeta (CA04: se diferencian por color, forma y texto). Si una zona tiene de los dos, se ponen una junto a la otra.
(() => {
  const contenedor = document.getElementById("mapa-concentracion");
  if (!contenedor || typeof L === "undefined") return;
  const celdas = JSON.parse(document.getElementById("celdas-concentracion").textContent);
  const puntos = JSON.parse(document.getElementById("calor-concentracion")?.textContent || "[]");
  const maximo = Math.max(1, Number(contenedor.dataset.maximo));
  // Sin la marca «Leaflet» (su licencia BSD-2 no la exige en pantalla); el crédito de OpenStreetMap sí se mantiene.
  L.Control.Attribution.prototype.options.prefix = false;
  const mapa = L.map(contenedor, { zoomControl: false }).setView([Number(contenedor.dataset.centroLat), Number(contenedor.dataset.centroLon)], Number(contenedor.dataset.zoom));
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);

  // ---------- Calor ----------
  // Escala térmica (de frío a caliente). La transparencia crece con el calor: donde hay poco, la ciudad se sigue viendo.
  const ESCALA = [
    [0.0, "rgba(38, 24, 120, 0)"],
    [0.12, "rgba(48, 30, 150, 0.5)"],
    [0.3, "rgba(118, 34, 170, 0.72)"],
    [0.5, "rgba(206, 46, 150, 0.84)"],
    [0.68, "rgba(244, 118, 32, 0.9)"],
    [0.84, "rgba(251, 196, 40, 0.94)"],
    [1.0, "rgba(255, 248, 205, 0.97)"],
  ];
  const paleta = (() => {
    const lienzo = document.createElement("canvas");
    lienzo.width = 256;
    lienzo.height = 1;
    const ctx = lienzo.getContext("2d");
    const degradado = ctx.createLinearGradient(0, 0, 256, 0);
    for (const [parada, color] of ESCALA) degradado.addColorStop(parada, color);
    ctx.fillStyle = degradado;
    ctx.fillRect(0, 0, 256, 1);
    return ctx.getImageData(0, 0, 256, 1).data;
  })();

  const pesoMaximo = Math.max(1, ...puntos.map((p) => p.casos));
  const CapaCalor = L.Layer.extend({
    onAdd(m) {
      this.lienzo = L.DomUtil.create("canvas", "calor-concentracion leaflet-zoom-hide");
      m.getPanes().overlayPane.appendChild(this.lienzo);
      m.on("moveend zoomend resize", this.dibujar, this);
      this.dibujar();
    },
    onRemove(m) {
      m.off("moveend zoomend resize", this.dibujar, this);
      this.lienzo.remove();
    },
    dibujar() {
      const m = this._map;
      const tamano = m.getSize();
      const lienzo = this.lienzo;
      lienzo.width = tamano.x;
      lienzo.height = tamano.y;
      L.DomUtil.setPosition(lienzo, m.containerPointToLayerPoint([0, 0]));
      // El halo mide unos 170 m en el terreno: al acercarse crece, al alejarse los vecinos se funden en una sola mancha.
      const metrosPorPixel = (40075016.686 * Math.cos((m.getCenter().lat * Math.PI) / 180)) / 2 ** (m.getZoom() + 8);
      const radio = Math.max(14, Math.min(80, 200 / metrosPorPixel));
      const ctx = lienzo.getContext("2d", { willReadFrequently: true });
      ctx.clearRect(0, 0, tamano.x, tamano.y);
      // 1. Intensidad acumulada en el canal alfa: cada punto, un halo radial con el peso de sus casos.
      for (const p of puntos) {
        const { x, y } = m.latLngToContainerPoint([p.lat, p.lon]);
        if (x < -radio || y < -radio || x > tamano.x + radio || y > tamano.y + radio) continue;
        const halo = ctx.createRadialGradient(x, y, 0, x, y, radio);
        halo.addColorStop(0, "rgba(0,0,0,1)");
        halo.addColorStop(1, "rgba(0,0,0,0)");
        // Cada caso aporta poco: el amarillo casi blanco queda para donde muchos se superponen, y la gradación se lee.
        ctx.globalAlpha = Math.min(1, 0.12 + 0.55 * Math.sqrt(p.casos / pesoMaximo));
        ctx.fillStyle = halo;
        ctx.fillRect(x - radio, y - radio, radio * 2, radio * 2);
      }
      ctx.globalAlpha = 1;
      // 2. Color: la intensidad de cada píxel elige su tono en la escala térmica.
      const imagen = ctx.getImageData(0, 0, tamano.x, tamano.y);
      const datos = imagen.data;
      for (let i = 3; i < datos.length; i += 4) {
        const intensidad = datos[i];
        if (!intensidad) continue;
        const j = intensidad * 4;
        datos[i - 3] = paleta[j];
        datos[i - 2] = paleta[j + 1];
        datos[i - 1] = paleta[j + 2];
        datos[i] = paleta[j + 3];
      }
      ctx.putImageData(imagen, 0, 0);
    },
  });
  const calor = new CapaCalor();
  if (puntos.length) calor.addTo(mapa);

  // ---------- Cifras por zona ----------
  const MINIMO_CIFRA = 3, MAXIMO_CIFRAS = 20;
  const cifras = L.layerGroup().addTo(mapa);
  const zonas = new Set();
  const marcas = [];
  const conCifra = celdas.filter((c) => c.casos >= MINIMO_CIFRA).sort((a, b) => b.casos - a.casos).slice(0, MAXIMO_CIFRAS);
  for (const celda of conCifra) {
    const proporcion = celda.casos / maximo;
    const lado = Math.round(26 + 14 * Math.sqrt(proporcion));
    const centro = [(celda.sur + celda.norte) / 2, (celda.oeste + celda.este) / 2];
    const zona = centro.join(",");
    const compartida = zonas.has(zona);
    zonas.add(zona);
    const sos = celda.origen === "SOS";
    const texto = `${celda.zona} · ${sos ? "SOS" : "Reportes"}: ${celda.casos} ${celda.casos === 1 ? "caso" : "casos"}`;
    const marca = L.marker(centro, {
      icon: L.divIcon({
        className: `celda-concentracion celda-concentracion--${sos ? "sos" : "reporte"}`,
        // --i escalona la aparición de las insignias (una sola vez, al generar el mapa).
        html: `<span style="--i:${Math.min(marcas.length, 12)}">${celda.casos}</span>`,
        iconSize: [lado, lado],
        // La segunda insignia de una misma zona se desplaza para que las dos se vean.
        iconAnchor: [compartida ? -2 : lado / 2, lado / 2],
      }),
      title: texto,
      alt: texto,
      keyboard: true,
    }).bindPopup(texto);
    marca.addTo(cifras);
    marcas.push(marca);
  }
  // El encuadre abarca todo lo que tiene calor, no solo las zonas con cifra.
  const encuadre = puntos.length ? puntos.map((p) => [p.lat, p.lon]) : celdas.map((c) => [(c.sur + c.norte) / 2, (c.oeste + c.este) / 2]);
  if (encuadre.length) mapa.fitBounds(L.latLngBounds(encuadre).pad(0.12), { maxZoom: 15 });

  // ---------- Capas ----------
  for (const control of document.querySelectorAll("[data-capa]")) {
    control.addEventListener("change", () => {
      const capa = control.dataset.capa === "calor" ? calor : cifras;
      if (control.checked) capa.addTo(mapa);
      else mapa.removeLayer(capa);
    });
  }

  // Acceso para la verificación automatizada (como window.cadCentral).
  window.cadConcentracion = { mapa, calor, cifras };
})();
