// «Incidencias recurrentes» como mapa de los puntos (16/11/2026): un punto del mapa y su fila de la lista se señalan juntos. Pasar
// por la fila (con el cursor o con el teclado) resalta el punto; pasar por el punto, la fila. Los marcadores los dibuja analitica.js
// antes de este script; su número va en el contenido del marcador (data-punto-mapa). Elegir una fila centra el mapa en su punto.
(() => {
  const tablero = document.querySelector("[data-repite]");
  if (!tablero) return;
  const filas = [...tablero.querySelectorAll(".punto-b[data-punto]")];
  const marcadores = () => [...tablero.querySelectorAll("[data-punto-mapa]")].map((n) => ({ numero: n.dataset.puntoMapa, elemento: n.closest(".punto-a") }));

  function resaltar(numero) {
    for (const fila of filas) fila.classList.toggle("foco", fila.dataset.punto === numero);
    for (const { numero: n, elemento } of marcadores()) elemento?.classList.toggle("punto-a--foco", n === numero);
  }

  // Elegir una fila (clic o Enter) centra el mapa en su punto y lo acerca, sin perder el resaltado.
  const contenedor = document.getElementById("mapa-zonas");
  const datos = (() => { try { return JSON.parse(contenedor?.querySelector("[data-puntos]")?.textContent ?? "[]"); } catch { return []; } })();
  const reducido = matchMedia("(prefers-reduced-motion: reduce)").matches;
  function centrar(numero) {
    const p = datos.find((d) => String(d.numero) === numero);
    const mapa = contenedor?.mapa;
    if (!p || !mapa) return;
    const zoom = Math.max(mapa.getZoom(), 16);
    if (reducido) mapa.setView([p.lat, p.lon], zoom, { animate: false });
    else mapa.flyTo([p.lat, p.lon], zoom, { duration: 0.6 });
    for (const fila of filas) fila.classList.toggle("elegida", fila.dataset.punto === numero);
  }

  for (const fila of filas) {
    fila.addEventListener("click", () => centrar(fila.dataset.punto));
    fila.addEventListener("mouseenter", () => resaltar(fila.dataset.punto));
    fila.addEventListener("focus", () => resaltar(fila.dataset.punto));
    fila.addEventListener("mouseleave", () => resaltar(null));
    fila.addEventListener("blur", () => resaltar(null));
  }
  for (const { numero, elemento } of marcadores()) {
    elemento?.addEventListener("mouseenter", () => resaltar(numero));
    elemento?.addEventListener("mouseleave", () => resaltar(null));
  }
})();
