// Deudas conocidas del diseño anterior al rediseño. Se anotan en el informe sin hacer fallar la revisión;
// cualquier hallazgo que no esté aquí sí falla. Al corregir una deuda, bórrala: si reaparece, la revisión fallará.
//   axe:        reglas de axe-core aceptadas en esa pantalla.
//   objetivos:  fragmentos de la descripción de controles de menos de 24 px aceptados.
export const deudas = {
  central: {
    motivo: 'Dos marcadores de casos muy cercanos se superponen en el mapa (target-size). Cada marcador mide 36 × 32 px; falta agruparlos cuando se tocan. Los botones de la cola ya cumplen (fase 3).',
    axe: ['target-size'],
    objetivos: [],
  },
  'acompanamiento-detalle': {
    motivo: 'A 390 px la tabla de posiciones se desplaza en horizontal sin poder enfocarse con el teclado (falta tabindex y nombre en .tabla-contenedor). Fase 3.',
    axe: ['scrollable-region-focusable'],
    objetivos: [],
  },
};

export const deudaDe = (pantalla) => deudas[pantalla] ?? { axe: [], objetivos: [] };
