// Catálogo de pantallas del panel que se revisan, por rol. «ruta» recibe los identificadores sembrados.
export const pantallas = [
  { nombre: 'ingreso', rol: null, ruta: () => '/panel/ingresar' },

  { nombre: 'admin-inicio', rol: 'admin', ruta: () => '/panel/admin' },
  { nombre: 'admin-cuentas', rol: 'admin', ruta: () => '/panel/admin/cuentas' },
  { nombre: 'admin-nueva-cuenta', rol: 'admin', ruta: () => '/panel/admin/cuentas/nueva' },
  { nombre: 'acceso-denegado', rol: 'admin', ruta: () => '/panel/central' },

  // La central se mueve en tiempo real; se espera a la conexión antes de capturar.
  { nombre: 'central', rol: 'operador', ruta: () => '/panel/central', tiempoReal: true },
  { nombre: 'sos-pendiente', rol: 'operador', ruta: (ids) => `/panel/central/sos/${ids.sosPendiente}` },
  { nombre: 'sos-cerrado', rol: 'operador', ruta: (ids) => `/panel/central/sos/${ids.sosCerrado}` },
  { nombre: 'historial-sos', rol: 'operador', ruta: (ids) => `/panel/casos/${ids.sosCerrado}/historial` },
  { nombre: 'reportes', rol: 'operador', ruta: () => '/panel/reportes' },
  { nombre: 'reporte-recibido', rol: 'operador', ruta: (ids) => `/panel/reportes/${ids.reporteRecibido}` },
  { nombre: 'reporte-en-revision', rol: 'operador', ruta: (ids) => `/panel/reportes/${ids.reporteEnRevision}` },
  { nombre: 'reporte-admitido', rol: 'operador', ruta: (ids) => `/panel/reportes/${ids.reporteAdmitido}` },
  { nombre: 'casos', rol: 'operador', ruta: () => '/panel/casos' },
  { nombre: 'acompanamientos', rol: 'operador', ruta: () => '/panel/acompanamientos' },
  { nombre: 'acompanamiento-detalle', rol: 'operador', ruta: (ids) => `/panel/acompanamientos/${ids.acompanamiento}` },
  { nombre: 'acompanamientos-historial', rol: 'operador', ruta: () => '/panel/acompanamientos/historial' },
  { nombre: 'mapa-concentracion', rol: 'operador', ruta: () => '/panel/mapa-concentracion' },
];
