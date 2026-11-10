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

    const etiqueta = document.createElement("span");
    etiqueta.className = "mapa__ambito";
    etiqueta.textContent = "Ámbito actual · José Luis Bustamante y Rivero";
    contenedor.appendChild(etiqueta);

    const halo = mapa.createPane("limiteHalo");
    halo.style.zIndex = 450;
    halo.style.pointerEvents = "none";
    const trazo = mapa.createPane("limiteDistrito");
    trazo.style.zIndex = 451;
    trazo.style.pointerEvents = "none";

    cargar().then((geojson) => {
      if (!mapa.getContainer().isConnected) return;
      L.geoJSON(geojson, { pane: "limiteHalo", interactive: false,
        style: { color: "#10243a", weight: 7, opacity: 0.92, fill: false } }).addTo(mapa);
      L.geoJSON(geojson, { pane: "limiteDistrito", interactive: false,
        style: { color: "#f7d66d", weight: 3, opacity: 1, fill: false } }).addTo(mapa);
    }).catch((error) => console.error("Límite del distrito:", error));
  });
})();
