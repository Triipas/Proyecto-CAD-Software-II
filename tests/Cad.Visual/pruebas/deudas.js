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
};

export const deudaDe = (pantalla) => deudas[pantalla] ?? { axe: [], objetivos: [] };
