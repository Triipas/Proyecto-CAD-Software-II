// «Incidencias recurrentes» como mapa de los puntos (16/11/2026): un punto del mapa y su fila de la lista se señalan juntos. Pasar
// por la fila (con el cursor o con el teclado) resalta el punto; pasar por el punto, la fila. Los marcadores los dibuja analitica.js
// antes de este script; su número va en el contenido del marcador (data-punto-mapa).
(() => {
  const tablero = document.querySelector("[data-repite]");
  if (!tablero) return;
  const filas = [...tablero.querySelectorAll(".punto-b[data-punto]")];
  const marcadores = () => [...tablero.querySelectorAll("[data-punto-mapa]")].map((n) => ({ numero: n.dataset.puntoMapa, elemento: n.closest(".punto-a") }));

  function resaltar(numero) {
    for (const fila of filas) fila.classList.toggle("foco", fila.dataset.punto === numero);
    for (const { numero: n, elemento } of marcadores()) elemento?.classList.toggle("punto-a--foco", n === numero);
  }

  for (const fila of filas) {
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
