// Marcadores del mapa (27/10/2026): una sola familia para la central y los mapas de detalle. La forma dice qué es (SOS:
// cuadrado redondeado con sirena; reporte: círculo con documento; sereno: escudo con su silueta) y el relleno, su estado
// (lleno: sin asignar; blanco con borde: en curso). Todos llevan el borde blanco y la sombra, y su icono va al centro: el
// ancla de Leaflet cae justo en el punto.
window.cadMarcadores = (() => {
  const icono = (trazos) => `<svg class="icono" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${trazos}</svg>`;
  const SIRENA = icono('<path d="M6 18v-5a6 6 0 0 1 12 0v5"/><path d="M4 18h16v3H4zM12 2v2M4.2 5.2l1.4 1.4M19.8 5.2l-1.4 1.4"/>');
  const DOCUMENTO = icono('<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>');

  // El escudo del sereno lleva su silueta; gris cuando no está disponible.
  const escudo = (disponible) => `<svg viewBox="0 0 32 36" aria-hidden="true" focusable="false">`
    + `<path d="M16 2 30 8v9c0 9-6 15-14 17C8 32 2 26 2 17V8z" fill="${disponible ? "var(--mapa-sereno)" : "var(--mapa-sereno-no-disponible)"}" stroke="#fff" stroke-width="3" stroke-linejoin="round"/>`
    + `<circle cx="16" cy="14" r="3.6" fill="#fff"/><path d="M9.6 25.2c1.1-3.5 3.6-5.2 6.4-5.2s5.3 1.7 6.4 5.2" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round"/></svg>`;

  // Opciones de L.divIcon para un caso. El cuadrado y el círculo se compensan a la vista: el círculo es 2 px más ancho.
  const caso = (reporte, clases = "") => ({
    className: `${reporte ? "marcador-reporte" : "marcador-sos"}${clases ? ` ${clases}` : ""}`,
    html: reporte ? DOCUMENTO : SIRENA,
    iconSize: reporte ? [34, 34] : [32, 32],
    iconAnchor: reporte ? [17, 17] : [16, 16],
    popupAnchor: [0, -20],
  });

  const sereno = (disponible) => ({
    className: `marcador-sereno${disponible ? "" : " marcador-sereno--no-disponible"}`,
    html: escudo(disponible),
    iconSize: [30, 34],
    iconAnchor: [15, 17],
    popupAnchor: [0, -22],
  });

  return { SIRENA, DOCUMENTO, escudo, caso, sereno };
})();
