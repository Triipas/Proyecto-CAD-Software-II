// Siembra un escenario ficticio y estable para revisar todas las pantallas del panel:
// cuentas institucionales, un SOS cerrado, un SOS pendiente, tres reportes en distintos estados
// y un acompañamiento activo. Los datos son ficticios; las contraseñas se generan en cada ejecución.
import { randomUUID } from 'node:crypto';
import { LIMA, URL_BASE, sesion } from './entorno.js';

const clave = () => `Clave-${randomUUID().slice(0, 8)}9`;
const ahora = () => new Date().toISOString();
const cerca = (dLat, dLon) => ({ latitud: LIMA.latitud + dLat, longitud: LIMA.longitud + dLon });

async function api(ruta, { cuerpo, token, metodo = 'POST' } = {}) {
  const respuesta = await fetch(`${URL_BASE}${ruta}`, {
    method: metodo,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
  });
  const texto = await respuesta.text();
  if (!respuesta.ok) throw new Error(`${metodo} ${ruta} respondió ${respuesta.status}: ${texto}`);
  return texto ? JSON.parse(texto) : null;
}

const token = async (correo, contrasena) =>
  (await api('/api/auth/token', { cuerpo: { correo, contrasena } })).token;

// Con { provisional }, la cuenta entra con la contraseña del administrador y la cambia por la suya, como pide el panel.
async function ingresarPanel(contexto, correo, contrasena, { provisional } = {}) {
  const pagina = await contexto.newPage();
  await pagina.goto(`${URL_BASE}/panel/ingresar`);
  await pagina.getByLabel('Correo electrónico').fill(correo);
  await pagina.getByLabel('Contraseña').fill(provisional ?? contrasena);
  await pagina.getByRole('button', { name: 'Ingresar' }).click();
  await pagina.waitForURL((url) => !url.pathname.endsWith('/ingresar'));
  if (provisional) {
    await pagina.getByLabel('Contraseña actual').fill(provisional);
    await pagina.getByLabel('Contraseña nueva', { exact: true }).fill(contrasena);
    await pagina.getByLabel('Repite la contraseña nueva').fill(contrasena);
    await pagina.getByRole('button', { name: 'Guardar contraseña' }).click();
    await pagina.waitForURL((url) => !/contrasena/i.test(url.pathname));
  }
  return pagina;
}

// Cuentas del personal (22/10/2026): DNI obligatorio y contraseña inicial provisional, que se cambia al primer ingreso.
async function crearCuenta(pagina, { nombres, apellidos, dni, correo, telefono, rol, contrasena }) {
  await pagina.goto(`${URL_BASE}/panel/admin/cuentas/nueva`);
  await pagina.getByLabel('Nombres').fill(nombres);
  await pagina.getByLabel('Apellidos').fill(apellidos);
  await pagina.getByLabel('DNI').fill(dni);
  await pagina.getByLabel('Correo electrónico').fill(correo);
  await pagina.getByLabel('Teléfono móvil').fill(telefono);
  await pagina.getByLabel('Rol').selectOption(rol);
  await pagina.getByLabel('Contraseña inicial').fill(contrasena);
  await pagina.getByRole('button', { name: 'Crear cuenta' }).click();
  await pagina.waitForURL((url) => !url.pathname.endsWith('/nueva'));
}

// El sereno cambia la provisional desde la app (API) antes de operar.
async function estrenarSereno(correo, provisional, contrasena) {
  await api('/api/cuentas/contrasena', {
    token: await token(correo, provisional),
    cuerpo: { actual: provisional, nueva: contrasena, confirmacion: contrasena },
  });
}

export async function sembrar(navegador, secretos) {
  const cuentas = {
    admin: secretos.admin,
    operador: { correo: 'olga.operadora@cad.test', clave: clave() },
    serena: { correo: 'sara.serena@cad.test', clave: clave() },
    suplente: { correo: 'pedro.suplente@cad.test', clave: clave() },
    ciudadana: { correo: 'ana.ficticia@correo.test', clave: clave() },
    ciudadano: { correo: 'beto.ficticio@correo.test', clave: clave() },
  };

  // 1. El administrador crea las cuentas institucionales desde el panel.
  const contextoAdmin = await navegador.newContext();
  const admin = await ingresarPanel(contextoAdmin, cuentas.admin.correo, cuentas.admin.clave);
  const provisionales = { operador: clave(), serena: clave(), suplente: clave() };
  await crearCuenta(admin, { nombres: 'Olga', apellidos: 'Operadora Ficticia', dni: '40000001', correo: cuentas.operador.correo, telefono: '911111111', rol: 'Operador', contrasena: provisionales.operador });
  await crearCuenta(admin, { nombres: 'Sara', apellidos: 'Serena Ficticia', dni: '40000002', correo: cuentas.serena.correo, telefono: '922222222', rol: 'Sereno', contrasena: provisionales.serena });
  await crearCuenta(admin, { nombres: 'Pedro', apellidos: 'Suplente Ficticio', dni: '40000003', correo: cuentas.suplente.correo, telefono: '933333333', rol: 'Sereno', contrasena: provisionales.suplente });
  await contextoAdmin.storageState({ path: sesion('admin') });
  await contextoAdmin.close();
  await estrenarSereno(cuentas.serena.correo, provisionales.serena, cuentas.serena.clave);
  await estrenarSereno(cuentas.suplente.correo, provisionales.suplente, cuentas.suplente.clave);

  // 2. Ciudadanos registrados desde la app (API).
  await api('/api/cuentas/registro', { cuerpo: { nombres: 'Ana', apellidos: 'Ficticia', correo: cuentas.ciudadana.correo, telefono: '900000001', contrasena: cuentas.ciudadana.clave } });
  await api('/api/cuentas/registro', { cuerpo: { nombres: 'Beto', apellidos: 'Ficticio', correo: cuentas.ciudadano.correo, telefono: '900000002', contrasena: cuentas.ciudadano.clave } });
  const ana = await token(cuentas.ciudadana.correo, cuentas.ciudadana.clave);
  const beto = await token(cuentas.ciudadano.correo, cuentas.ciudadano.clave);
  const sara = await token(cuentas.serena.correo, cuentas.serena.clave);

  // 3. Sara activa su estado operativo y envía su posición.
  await api('/api/sereno/estado', { metodo: 'PUT', token: sara, cuerpo: { operativo: true, ubicacionAutorizada: true } });
  await api('/api/sereno/posiciones', { token: sara, cuerpo: { ...cerca(0.004, -0.003), precisionMetros: 8, obtenidaEn: ahora() } });

  // 4. Primer SOS de Ana: se verifica, clasifica, asigna, atiende y cierra.
  const sosCerrado = await api('/api/sos', { token: ana, cuerpo: { idSolicitud: randomUUID(), activadoEn: ahora(), ubicacion: { ...LIMA, precisionMetros: 5, obtenidaEn: ahora() } } });
  const contextoOperador = await navegador.newContext();
  const operador = await ingresarPanel(contextoOperador, cuentas.operador.correo, cuentas.operador.clave, { provisional: provisionales.operador });
  await operador.goto(`${URL_BASE}/panel/central/sos/${sosCerrado.id}`);
  await operador.getByLabel('Contactado').check();
  await operador.getByLabel('Información del hecho (opcional)').fill('La ciudadana indica que un desconocido la sigue desde el paradero (dato ficticio).');
  await operador.getByLabel(/Referencia de ubicación/).fill('Frente a la farmacia de la avenida principal');
  await operador.getByRole('button', { name: 'Registrar intento' }).click();
  await operador.waitForLoadState('networkidle');
  await operador.getByLabel('Categoría', { exact: true }).selectOption({ index: 1 });
  // 27/10/2026: la clasificación registra también la prioridad y si hay heridos.
  await operador.getByLabel('Prioridad', { exact: true }).selectOption('Media');
  await operador.getByLabel('¿Hay personas heridas?').selectOption('No');
  await operador.getByRole('button', { name: 'Clasificar el SOS' }).click();
  await operador.waitForLoadState('networkidle');
  await operador.getByRole('radio', { name: /Sara/ }).check();
  await operador.getByRole('button', { name: 'Asignar el caso' }).click();
  await operador.waitForLoadState('networkidle');

  const categoriasSereno = await api('/api/sereno/categorias', { metodo: 'GET', token: sara });
  await api(`/api/sereno/casos/${sosCerrado.id}/desplazamiento`, { token: sara });
  // Llega al lugar: la atención se inicia a menos de 150 m del punto del caso.
  await api('/api/sereno/posiciones', { token: sara, cuerpo: { ...cerca(0.0003, 0.0002), precisionMetros: 6, obtenidaEn: ahora() } });
  await api(`/api/sereno/casos/${sosCerrado.id}/atencion`, { token: sara });
  await api(`/api/sereno/casos/${sosCerrado.id}/cierre`, {
    token: sara,
    cuerpo: { categoriaId: categoriasSereno[0].id, resultadoAtencion: 'ResueltoEnElLugar', resultadoPublico: 'Se acompañó a la ciudadana hasta su domicilio y se le explicó cómo denunciar.', notasInternas: 'Sin incidencias (dato ficticio).' },
  });
  await api('/api/sereno/posiciones', { token: sara, cuerpo: { ...cerca(0.001, 0.001), precisionMetros: 6, obtenidaEn: ahora() } });

  // 5. Tres reportes de Ana con cada forma de indicar el lugar.
  const categorias = await api('/api/reportes/categorias', { metodo: 'GET', token: ana });
  const reporte = (indice, descripcion, ubicacion) => api('/api/reportes', {
    token: ana,
    cuerpo: { idSolicitud: randomUUID(), categoriaId: categorias[indice % categorias.length].id, descripcion, ocurridoAproximadamenteEn: ahora(), ubicacion },
  });
  const reporteAdmitido = await reporte(0, 'Robo de celular en el paradero (dato ficticio).', { fuente: 'Mapa', ...cerca(-0.006, 0.004), precisionMetros: null, referencia: null });
  const reporteEnRevision = await reporte(1, 'Grupo de personas consumiendo alcohol en el parque por la noche (dato ficticio).', { fuente: 'PosicionActual', ...cerca(0.007, 0.006), precisionMetros: 10, referencia: null });
  const reporteRecibido = await reporte(2, 'Luminaria apagada en el pasaje desde hace una semana (dato ficticio).', { fuente: 'Texto', latitud: null, longitud: null, precisionMetros: null, referencia: 'Pasaje al costado del mercado central' });

  // 6. El operador revisa dos reportes: admite el primero y deja el segundo en revisión.
  await operador.goto(`${URL_BASE}/panel/reportes/${reporteAdmitido.id}`);
  await operador.getByRole('button', { name: 'Iniciar la revisión' }).click();
  await operador.waitForLoadState('networkidle');
  await operador.getByLabel('Admitir', { exact: true }).check();
  await operador.locator('[name="Decision.Prioridad"]').selectOption('Baja');
  await operador.locator('[name="Decision.Heridos"]').selectOption('No');
  for (const campo of ['Accion', 'Motivo', 'Destino']) {
    const control = operador.locator(`[name="Decision.${campo}"]`);
    if (await control.count() && await control.isVisible()) await control.fill('Enviar un sereno a verificar la zona (dato ficticio).');
  }
  await operador.getByRole('button', { name: 'Registrar la decisión' }).click();
  await operador.waitForLoadState('networkidle');
  await operador.goto(`${URL_BASE}/panel/reportes/${reporteEnRevision.id}`);
  await operador.getByRole('button', { name: 'Iniciar la revisión' }).click();
  await operador.waitForLoadState('networkidle');

  // 7. Segundo SOS de Ana, pendiente: es lo primero que debe ver la central.
  const sosPendiente = await api('/api/sos', { token: ana, cuerpo: { idSolicitud: randomUUID(), activadoEn: ahora(), ubicacion: { ...cerca(0.002, -0.002), precisionMetros: 5, obtenidaEn: ahora() } } });

  // 8. Beto inicia un acompañamiento y envía su recorrido.
  const acompanamiento = await api('/api/acompanamiento', { token: beto, cuerpo: { idSolicitud: randomUUID(), finalidadAceptada: true, ubicacionDisponible: true } });
  for (const [i, delta] of [[0, 0], [1, 0.0008], [2, 0.0016]]) {
    await api(`/api/acompanamiento/${acompanamiento.id}/posiciones`, { token: beto, cuerpo: { ...cerca(-0.003 + delta, 0.002 + delta), precisionMetros: 5 + i, obtenidaEn: ahora() } });
  }

  await contextoOperador.storageState({ path: sesion('operador') });
  await contextoOperador.close();

  return {
    cuentas,
    ids: {
      sosCerrado: sosCerrado.id,
      sosPendiente: sosPendiente.id,
      reporteAdmitido: reporteAdmitido.id,
      reporteEnRevision: reporteEnRevision.id,
      reporteRecibido: reporteRecibido.id,
      acompanamiento: acompanamiento.id,
    },
  };
}
