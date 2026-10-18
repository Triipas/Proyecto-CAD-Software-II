// Deudas conocidas del diseño anterior al rediseño. Se anotan en el informe sin hacer fallar la revisión;
// cualquier hallazgo que no esté aquí sí falla. Al corregir una deuda, bórrala: si reaparece, la revisión fallará.
//   axe:        reglas de axe-core aceptadas en esa pantalla.
//   objetivos:  fragmentos de la descripción de controles de menos de 24 px aceptados.
export const deudas = {
  central: {
    motivo: 'Marcadores del mapa superpuestos y botones con aspecto de enlace en las tarjetas. Fase 3 (decisiones D2, D3 y D7).',
    axe: ['target-size'],
    objetivos: ['sos__ubicacion', 'sos__elegir'],
  },
  'acompanamiento-detalle': {
    motivo: 'A 390 px la tabla de posiciones se desplaza en horizontal sin poder enfocarse con el teclado (falta tabindex y nombre en .tabla-contenedor). Fase 3.',
    axe: ['scrollable-region-focusable'],
    objetivos: [],
  },
};

export const deudaDe = (pantalla) => deudas[pantalla] ?? { axe: [], objetivos: [] };
