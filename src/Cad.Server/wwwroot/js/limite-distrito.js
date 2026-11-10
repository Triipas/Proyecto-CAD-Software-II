// Ámbito de la demo: el mismo límite JLByR usado para generar los datos simulados.
// Se registra antes de que cada página cree su mapa Leaflet.
(() => {
  const url = new URL("../geo/limite-jlbyr.json", document.currentScript.src);
  let geometria;
  const cargar = () => geometria ??= fetch(url).then((respuesta) => {
    if (!respuesta.ok) throw new Error(`No se pudo cargar el límite del distrito (${respuesta.status})`);
    return respuesta.json();
  });

  L.Map.addInitHook(function () {
    const mapa = this;
    const contenedor = mapa.getContainer();
    // Los minimapas de detalle miden una calle; el contorno solo tiene sentido en los mapas de ámbito.
    if (!contenedor.matches("#mapa, #mapa-concentracion, #mapa-proyeccion, #mapa-zonas, [data-mapa]")) return;

    const halo = mapa.createPane("limiteHalo");
    halo.style.zIndex = 450;
    halo.style.pointerEvents = "none";
    const trazo = mapa.createPane("limiteDistrito");
    trazo.style.zIndex = 451;
    trazo.style.pointerEvents = "none";

    // En la ciudad de noche (concentración y proyección) el contorno es una línea clara con halo oscuro; en los mapas claros (la central
    // y la analítica) es una línea fina de azul apagado con un borde blanco: se lee como un límite, no compite con las rutas ni con los
    // casos (10/11/2026).
    const oscuro = contenedor.classList.contains("mapa--infrarrojo");
    const estiloHalo = oscuro
      ? { color: "#10243a", weight: 7, opacity: 0.92, fill: false }
      : { color: "#FFFFFF", weight: 5, opacity: 0.7, fill: false };
    const estiloTrazo = oscuro
      ? { color: "#f7d66d", weight: 3, opacity: 1, fill: false }
      : { color: "#5B6F8E", weight: 2, opacity: 0.9, fill: false };

    cargar().then((geojson) => {
      if (!mapa.getContainer().isConnected) return;
      L.geoJSON(geojson, { pane: "limiteHalo", interactive: false, style: estiloHalo }).addTo(mapa);
      L.geoJSON(geojson, { pane: "limiteDistrito", interactive: false, style: estiloTrazo }).addTo(mapa);
    }).catch((error) => console.error("Límite del distrito:", error));
  });
})();
