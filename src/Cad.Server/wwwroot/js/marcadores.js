// Marcadores del mapa (27/10/2026): una sola familia para la central y los mapas de detalle. La forma dice qué es (SOS:
// cuadrado redondeado con sirena; reporte: círculo con documento; sereno: camioneta verde) y el relleno, su estado
// (lleno: sin asignar; blanco con borde: en curso). Los casos llevan borde blanco y sombra; la camioneta, un distintivo de
// disponibilidad. El ancla de Leaflet mantiene cada marcador en su posición.
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

  // Atlas de ocho vistas 3D, en orden horario desde el norte. No se gira una imagen plana: cada vista conserva
  // la perspectiva de la cabina, la tolva y las ruedas. El escudo sigue disponible para el panel de serenos.
  const DIRECCIONES = ["N", "NE", "E", "SE", "S", "SO", "O", "NO"];
  const NOMBRES_DIRECCIONES = ["Norte", "Noreste", "Este", "Sureste", "Sur", "Suroeste", "Oeste", "Noroeste"];
  const normalizarRumbo = (rumbo) => ((rumbo % 360) + 360) % 360;
  const sector = (rumbo) => Math.round(normalizarRumbo(rumbo) / 45) % 8;
  const posicionSprite = (direccion) => `--camioneta-x:${(direccion % 4) * 100 / 3}%;--camioneta-y:${direccion < 4 ? 0 : 100}%`;

  const camioneta = (disponible, rumbo = 135) => {
    const direccion = sector(rumbo);
    return `<span class="camioneta${disponible ? "" : " camioneta--no-disponible"}" data-camioneta data-direccion="${DIRECCIONES[direccion]}" data-rumbo="sin-confirmar" style="${posicionSprite(direccion)}" aria-hidden="true">`
      + '<span class="camioneta__sprite"></span><span class="camioneta__estado">'
      + '<svg viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" focusable="false">'
      + '<path class="camioneta__libre" d="m3 6 2 2 4-4"/><path class="camioneta__ocupada" d="M3.5 6h5"/></svg></span></span>';
  };

  const sereno = (disponible, rumbo = 135) => ({
    className: `marcador-sereno marcador-sereno--vehiculo${disponible ? "" : " marcador-sereno--no-disponible"}`,
    html: camioneta(disponible, rumbo),
    iconSize: [56, 56],
    iconAnchor: [28, 32],
    popupAnchor: [0, -26],
  });

  // Con o sin caso, el rumbo se deduce de posiciones observadas, nunca del destino. Se exige un desplazamiento de al menos
  // 4 m y mayor que la incertidumbre de ambas lecturas GPS. La banda de 5° evita alternar vistas cerca de un límite.
  // La referencia queda fuera del marcador: cambiar de disponibilidad o llegar a un caso no pierde la última dirección.
  function orientarSereno(anterior, punto, lectura = {}) {
    const actual = Array.isArray(punto) ? punto : [punto?.lat, punto?.lng];
    if (actual.length !== 2 || !actual.every(Number.isFinite) || Math.abs(actual[0]) > 90 || Math.abs(actual[1]) > 180) return anterior;
    const precision = Number.isFinite(lectura.precisionMetros) && lectura.precisionMetros >= 0 ? lectura.precisionMetros : 0;
    const instante = Date.parse(lectura.recibidaEn);
    const ultimaLectura = Number.isFinite(instante) ? instante : anterior?.ultimaLectura;
    if (!anterior) return { referencia: [...actual], precisionMetros: precision, ultimaLectura, rumbo: 135, direccion: 3, confirmada: false };
    // Un refresco sin posición nueva, o una respuesta atrasada, no debe orientar de nuevo el vehículo.
    if (Number.isFinite(instante) && Number.isFinite(anterior.ultimaLectura) && instante <= anterior.ultimaLectura) return anterior;
    // Tras más de dos minutos sin muestras no se interpreta el salto como un trayecto recto. Se conserva el último rumbo
    // conocido y la nueva posición pasa a ser la referencia para el siguiente desplazamiento, también sin caso asignado.
    if (Number.isFinite(instante) && Number.isFinite(anterior.ultimaLectura) && instante - anterior.ultimaLectura > 120000)
      return { ...anterior, referencia: [...actual], precisionMetros: precision, ultimaLectura, interrumpida: anterior.confirmada };
    const radianes = Math.PI / 180;
    const norte = (actual[0] - anterior.referencia[0]) * radianes;
    const este = (actual[1] - anterior.referencia[1]) * radianes
      * Math.cos((actual[0] + anterior.referencia[0]) * radianes / 2);
    const umbral = Math.max(4, (anterior.precisionMetros ?? 0) + precision);
    if (Math.hypot(norte, este) * 6371000 < umbral)
      return Number.isFinite(instante) ? { ...anterior, ultimaLectura } : anterior;
    const rumbo = normalizarRumbo(Math.atan2(este, norte) / radianes);
    const diferencia = Math.abs(((rumbo - anterior.direccion * 45 + 540) % 360) - 180);
    const direccion = !anterior.confirmada || diferencia > 27.5 ? sector(rumbo) : anterior.direccion;
    return { referencia: [...actual], precisionMetros: precision, ultimaLectura, rumbo, direccion, confirmada: true, interrumpida: false };
  }

  const textoOrientacion = (orientacion) => !orientacion?.confirmada ? "Sin rumbo confirmado"
    : `${NOMBRES_DIRECCIONES[orientacion.direccion]} · ${orientacion.interrumpida ? "último rumbo conocido" : "último desplazamiento"}`;

  function actualizarSereno(marcador, disponible, orientacion) {
    const elemento = marcador.getElement();
    const vehiculo = elemento?.querySelector("[data-camioneta]");
    if (!vehiculo) return;
    const direccion = orientacion?.direccion ?? 3;
    vehiculo.classList.toggle("camioneta--no-disponible", !disponible);
    elemento.classList.toggle("marcador-sereno--no-disponible", !disponible);
    vehiculo.dataset.rumbo = orientacion?.confirmada ? (orientacion.interrumpida ? "ultimo-conocido" : "observado") : "sin-confirmar";
    if (vehiculo.dataset.direccion === DIRECCIONES[direccion]) return;
    vehiculo.dataset.direccion = DIRECCIONES[direccion];
    vehiculo.style.setProperty("--camioneta-x", `${(direccion % 4) * 100 / 3}%`);
    vehiculo.style.setProperty("--camioneta-y", `${direccion < 4 ? 0 : 100}%`);
  }

  return { SIRENA, DOCUMENTO, escudo, caso, sereno, camioneta, orientarSereno, actualizarSereno, textoOrientacion };
})();
