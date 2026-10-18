// HU38 — acompañamientos activos: la lista se vuelve a pedir al servidor cada 10 s (la base de datos es la fuente de verdad)
// y la antigüedad de cada posición avanza cada segundo. Una posición desactualizada (DP12) o una sesión sin conexión (DP20) se
// dibuja en gris y nunca como posición actual (CA03).
(() => {
  const region = document.querySelector("[data-region-acompanamientos]");
  const contenedor = document.getElementById("mapa");
  if (!region || !contenedor || typeof L === "undefined") return;

  const mapa = L.map(contenedor).setView([Number(contenedor.dataset.centroLat), Number(contenedor.dataset.centroLon)], Number(contenedor.dataset.zoom));
  L.tileLayer(contenedor.dataset.teselas, {
    maxZoom: 19,
    attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">colaboradores de OpenStreetMap</a>',
  }).addTo(mapa);
  const marcadores = [];

  const escapar = (texto) => String(texto ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

  function dibujar() {
    for (const m of marcadores.splice(0)) m.remove();
    for (const item of region.querySelectorAll("[data-acompanamiento]")) {
      if (!item.dataset.lat) continue;
      const gris = item.dataset.desactualizada === "true" || item.dataset.sinConexion === "true";
      const texto = `Acompañamiento de ${item.dataset.ciudadano}${gris ? " (última posición conocida)" : ""}`;
      marcadores.push(L.marker([Number(item.dataset.lat), Number(item.dataset.lon)], {
        icon: L.divIcon({ className: `marcador-acompanamiento${gris ? " marcador-acompanamiento--desactualizado" : ""}`, html: "<span>A</span>", iconSize: [30, 30], iconAnchor: [15, 15] }),
        title: texto,
        alt: texto,
        keyboard: true,
      })
        .bindPopup(`<strong>Acompañamiento</strong><br>${escapar(item.dataset.ciudadano)}<br>${gris ? "Última posición conocida, no actual" : "Posición actual"}`)
        .addTo(mapa));
    }
  }

  // «hace 12 s» / «hace 3 min 5 s», como el servidor.
  function antiguedad(desde) {
    const segundos = Math.max(0, Math.floor((Date.now() - new Date(desde).getTime()) / 1000));
    return segundos < 60 ? `hace ${segundos} s` : `hace ${Math.floor(segundos / 60)} min ${segundos % 60} s`;
  }

  setInterval(() => {
    for (const span of region.querySelectorAll("[data-antiguedad]")) span.textContent = antiguedad(span.dataset.desde);
  }, 1000);

  async function actualizar() {
    try {
      const respuesta = await fetch("/panel/acompanamientos?handler=Lista", { credentials: "same-origin", headers: { Accept: "text/html" } });
      if (respuesta.redirected || !respuesta.ok) return; // sesión terminada o error: se conserva lo último mostrado
      region.innerHTML = await respuesta.text();
      dibujar();
    } catch {
      // Sin conexión con el servidor: se reintenta en la siguiente vuelta.
    }
  }

  dibujar();
  setInterval(actualizar, 10000);
})();
