// HU48 (S6-T20) — mapa de la vista «Proyección»: cada zona de unos 550 m se pinta según su nivel (alto, medio, bajo) y las de nivel
// alto y medio llevan además el nivel escrito (CA08: no solo color). Una zona de nivel alto sin sereno operativo cerca lleva el borde
// discontinuo. Solo celdas: el mapa no recibe ni muestra casos (CA03).
(() => {
  const contenedor = document.getElementById("mapa-proyeccion");
  if (!contenedor || typeof L === "undefined") return;
  const celdas = JSON.parse(document.getElementById("celdas-proyeccion")?.textContent || "[]");

  L.Control.Attribution.prototype.options.prefix = false;
  const mapa = L.map(contenedor, { zoomControl: false }).setView([Number(contenedor.dataset.centroLat), Number(contenedor.dataset.centroLon)], Number(contenedor.dataset.zoom));
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  L.control.zoom({ position: "bottomright", zoomInTitle: "Acercar", zoomOutTitle: "Alejar" }).addTo(mapa);

  const estilos = {
    Alto: { color: "#9a3412", fillColor: "#f97316", fillOpacity: 0.55, weight: 2 },
    Medio: { color: "#a16207", fillColor: "#facc15", fillOpacity: 0.4, weight: 1.5 },
    Bajo: { color: "#64748b", fillColor: "#cbd5e1", fillOpacity: 0.18, weight: 0.5 },
  };
  const texto = { Alto: "Alto", Medio: "Medio", Bajo: "Bajo" };
  const formato = new Intl.NumberFormat("es-PE", { maximumFractionDigits: 1, minimumFractionDigits: 1 });

  const limites = [];
  for (const c of celdas) {
    const caja = [[c.sur, c.oeste], [c.norte, c.este]];
    limites.push(...caja);
    const estilo = { ...estilos[c.nivel], dashArray: c.sinCobertura ? "6 4" : null };
    L.rectangle(caja, estilo)
      .bindTooltip(`${c.zona}: nivel ${texto[c.nivel].toLowerCase()}, ${formato.format(c.esperados)} casos esperados${c.sinCobertura ? " · sin cobertura cercana" : ""}`)
      .addTo(mapa);
    if (c.nivel !== "Bajo") {
      L.marker([(c.sur + c.norte) / 2, (c.oeste + c.este) / 2], {
        interactive: false,
        keyboard: false,
        icon: L.divIcon({ className: `etiqueta-nivel etiqueta-nivel--${c.nivel.toLowerCase()}`, html: texto[c.nivel], iconSize: null }),
      }).addTo(mapa);
    }
  }
  // El contenedor toma su tamaño final cuando termina de cargar la hoja de estilos: se recalcula antes de encuadrar las zonas.
  const encuadrar = () => {
    mapa.invalidateSize();
    if (limites.length > 0) mapa.fitBounds(limites, { padding: [24, 24], maxZoom: 15 });
  };
  encuadrar();
  if (document.readyState !== "complete") window.addEventListener("load", encuadrar, { once: true });
  new ResizeObserver(() => mapa.invalidateSize()).observe(contenedor);
})();
