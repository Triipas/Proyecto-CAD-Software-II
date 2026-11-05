// Datos de demostración para la central (05/11/2026): 20 reportes en varios estados y 5 SOS, alrededor del centro del mapa.
// Crea ciudadanos ficticios por la API de la app y lleva cada caso a su estado con los mismos formularios del panel que usa un
// operador (revisar, decidir, asignar, verificar y clasificar). Todo queda marcado «(dato de demostración)».
//
//   CAD_URL=https://<tu-servicio>.onrender.com CAD_OPERADOR="operador@...:clave" CAD_SERENOS="sereno1@...:clave;sereno2@...:clave" \
//     node herramientas/demo/sembrar.mjs
//
// Los serenos quedan operativos cerca de los casos; algunos reciben un caso y el resto queda disponible para mostrar el
// sereno más cercano. Después, mover-serenos.mjs los hace moverse solos.
import { ahora, alrededor, centroDelMapa, configuracion, crearApi, crearPanel, nuevoId } from './comun.mjs';

const { url, operador, serenos } = await configuracion({ pedirOperador: true, pedirSerenos: true });
if (!operador) {
  console.error('Falta la cuenta del operador: ejecútalo en una terminal para que la pida, o usa CAD_OPERADOR="correo:clave" (o CAD_CREDENCIALES con un JSON). Ver herramientas/demo/README.md.');
  process.exit(1);
}
const { api, token } = crearApi(url);
const panel = crearPanel(url);
const sello = Date.now().toString(36).slice(-5);

console.log(`Sembrando la demostración en ${url}`);
await panel.ingresar(operador.correo, operador.clave);
const centro = await centroDelMapa(panel);
console.log(`Centro del mapa: ${centro.latitud.toFixed(5)}, ${centro.longitud.toFixed(5)}`);

// ---------- Serenos: operativos y repartidos alrededor del centro ----------
const tokensSerenos = [];
for (const [i, s] of serenos.entries()) {
  const t = await token(s.correo, s.clave);
  await api('/api/sereno/estado', { metodo: 'PUT', token: t, cuerpo: { operativo: true, ubicacionAutorizada: true } });
  const punto = alrededor(centro, 1200);
  await api('/api/sereno/posiciones', { token: t, cuerpo: { ...punto, precisionMetros: 6, obtenidaEn: ahora() } });
  tokensSerenos.push({ ...s, token: t, punto });
  console.log(`  sereno ${i + 1}: ${s.correo} operativo`);
}

// ---------- Ciudadanos ficticios ----------
const nombres = [['Lucía', 'Quispe'], ['Jorge', 'Mamani'], ['Rosa', 'Huamán'], ['Carlos', 'Condori'], ['María', 'Flores'], ['Pedro', 'Ccama']];
const ciudadanos = [];
for (const [i, [n, a]] of nombres.entries()) {
  const correo = `demo.${n.toLowerCase().normalize('NFD').replace(/\p{M}/gu, '')}.${sello}@demo.cad`;
  const clave = `Demo-${nuevoId().slice(0, 8)}9`;
  await api('/api/cuentas/registro', { cuerpo: { nombres: n, apellidos: `${a} (demo)`, correo, telefono: `9${String(Math.floor(Math.random() * 1e8)).padStart(8, '0')}`, contrasena: clave } });
  ciudadanos.push({ nombre: `${n} ${a}`, token: await token(correo, clave) });
}
console.log(`  ${ciudadanos.length} ciudadanos ficticios registrados`);

// ---------- Reportes ----------
const categorias = await api('/api/reportes/categorias', { token: ciudadanos[0].token });
const cat = (parte) => categorias.find((c) => c.nombre.toLowerCase().includes(parte))?.id ?? categorias[0].id;
const demo = ' (dato de demostración)';
// [descripción, categoría, estado de destino, minutos atrás, solo referencia]
const reportes = [
  ['Dos sujetos en moto arrebatan celulares en el paradero de la avenida', 'robo', 'Recibido', 3],
  ['Grupo de jóvenes consumiendo alcohol en el parque desde la tarde', 'alcohol', 'Recibido', 6],
  ['Música a todo volumen en una vivienda, no dejan dormir a los vecinos', 'ruido', 'Recibido', 9],
  ['Auto sin placas estacionado hace dos días frente al colegio', 'sospechoso', 'Recibido', 12],
  ['Pintaron las paredes de la losa deportiva durante la noche', 'daño', 'Recibido', 15],
  ['Se escuchan gritos y golpes en el departamento del segundo piso', 'violencia', 'Recibido', 4],
  ['Hay un bache enorme en la avenida, varios autos se dañaron', 'otro', 'Recibido', 20, true],
  ['Una persona mayor desorientada camina por la pista', 'riesgo', 'En revisión', 18],
  ['Pelea entre varias personas a la salida de una discoteca', 'pelea', 'En revisión', 25],
  ['Venden droga en la esquina del mercado por las noches', 'droga', 'En revisión', 30],
  ['Me amenazaron por teléfono pidiendo dinero para no hacerme daño', 'extorsi', 'En revisión', 35],
  ['Robaron la batería de mi auto en la cochera del condominio', 'robo', 'Admitido', 40],
  ['Choque entre una combi y un mototaxi, hay personas en la vereda', 'accidente', 'Admitido', 45],
  ['Acosan a las alumnas que salen del instituto a mediodía', 'acoso', 'Admitido', 50],
  ['Jóvenes rompen las lunas de los autos estacionados en el pasaje', 'daño', 'Admitido', 55],
  ['Un hombre merodea las casas mirando por las ventanas', 'sospechoso', 'Admitido', 60],
  ['Asaltaron a una señora en la puerta de su casa', 'robo', 'Asignada', 70],
  ['Pelea en la vía pública con una persona herida', 'pelea', 'En camino', 80],
  ['Sale humo del techo de una casa abandonada', 'otro', 'Derivado', 90],
  ['asdf prueba de la aplicación', 'otro', 'Descartado', 95],
];

const disponiblesParaAsignar = () => tokensSerenos.filter((s) => !s.ocupado);
let creados = 0;
for (const [i, [descripcion, parte, estado, minutos, soloReferencia]] of reportes.entries()) {
  const ciudadano = ciudadanos[i % ciudadanos.length];
  const punto = alrededor(centro, 1500);
  const ubicacion = soloReferencia
    ? { fuente: 'Texto', latitud: null, longitud: null, precisionMetros: null, referencia: 'Avenida principal, a la altura del grifo' }
    : { fuente: i % 3 === 0 ? 'PosicionActual' : 'Mapa', ...punto, precisionMetros: i % 3 === 0 ? 12 : null, referencia: null };
  const { id } = await api('/api/reportes', {
    token: ciudadano.token,
    cuerpo: { idSolicitud: nuevoId(), categoriaId: cat(parte), descripcion: descripcion + demo, ocurridoAproximadamenteEn: new Date(Date.now() - minutos * 60_000).toISOString(), ubicacion },
  });
  const ruta = `/panel/reportes/${id}`;
  let logrado = estado;
  try {
    if (estado !== 'Recibido') await panel.enviar(ruta, 'IniciarRevision');
    if (['Admitido', 'Asignada', 'En camino', 'Derivado', 'Descartado'].includes(estado)) {
      const { html } = await panel.obtener(ruta);
      const elegir = (nombre, indice = 0) => panel.opciones(html, nombre).filter((o) => o.valor)[indice]?.valor ?? '';
      const campos = estado === 'Derivado'
        ? { 'Decision.Tipo': 'Derivacion', 'Decision.Destino': elegir('Decision.Destino'), 'Decision.Motivo': 'Hecho de otra entidad; se deriva para su atención' + demo, 'Decision.Accion': 'Se comunicó por teléfono a la entidad' + demo }
        : estado === 'Descartado'
          ? { 'Decision.Tipo': 'Descarte', 'Decision.MotivoDescarte': elegir('Decision.MotivoDescarte'), 'Decision.Motivo': 'Texto de prueba, sin un hecho que atender' + demo }
          : { 'Decision.Tipo': 'Admision', 'Decision.Prioridad': elegir('Decision.Prioridad', i % 3), 'Decision.Heridos': elegir('Decision.Heridos', 1), 'Decision.Motivo': 'Hecho de seguridad ciudadana ubicable; se envía un sereno' + demo };
      await panel.enviar(ruta, 'Decidir', campos);
    }
    if (['Asignada', 'En camino'].includes(estado)) {
      // Se deja al menos un sereno disponible para la demostración del más cercano.
      const libre = disponiblesParaAsignar().length > 1 ? disponiblesParaAsignar()[0] : null;
      if (libre) {
        await panel.enviar(ruta, 'Asignar', { 'Asignacion.SerenoId': await idSereno(libre), 'Asignacion.IdSolicitud': nuevoId(), 'Asignacion.Volver': 'detalle' });
        libre.ocupado = true;
        if (estado === 'En camino') await api(`/api/sereno/casos/${id}/desplazamiento`, { token: libre.token, metodo: 'POST' });
      } else logrado = 'Admitido (sin sereno libre)';
    }
    creados++;
    console.log(`  reporte ${String(i + 1).padStart(2)}: ${logrado.padEnd(11)} ${descripcion.slice(0, 60)}`);
  } catch (error) {
    console.log(`  reporte ${String(i + 1).padStart(2)}: se creó, pero no llegó a «${estado}»: ${error.message}`);
  }
}

// ---------- SOS: 3 recién llegados, 1 verificado y clasificado, 1 asignado ----------
const planSos = ['Pendiente', 'Pendiente', 'Pendiente', 'Clasificado', 'Asignada'];
for (const [i, estado] of planSos.entries()) {
  const ciudadano = ciudadanos[i];
  const punto = alrededor(centro, 1300);
  const { id } = await api('/api/sos', { token: ciudadano.token, cuerpo: { idSolicitud: nuevoId(), activadoEn: ahora(), ubicacion: { ...punto, precisionMetros: 8, obtenidaEn: ahora() } } });
  const ruta = `/panel/central/sos/${id}`;
  let logrado = estado;
  try {
    if (estado !== 'Pendiente') {
      await panel.enviar(ruta, null, { 'Formulario.IdSolicitud': nuevoId(), 'Formulario.Resultado': 'Contactado', 'Formulario.InformacionHecho': 'La ciudadana informa que la siguen dos personas' + demo });
      const { html } = await panel.obtener(ruta);
      const elegir = (nombre, indice = 0) => panel.opciones(html, nombre).filter((o) => o.valor)[indice]?.valor ?? '';
      await panel.enviar(ruta, 'Clasificar', { 'Clasificacion.CategoriaId': elegir('Clasificacion.CategoriaId', 1), 'Clasificacion.Prioridad': elegir('Clasificacion.Prioridad'), 'Clasificacion.Heridos': elegir('Clasificacion.Heridos', 1) });
    }
    if (estado === 'Asignada') {
      const libre = disponiblesParaAsignar().length > 1 ? disponiblesParaAsignar()[0] : null;
      if (libre) {
        await panel.enviar(ruta, 'Asignar', { 'Asignacion.SerenoId': await idSereno(libre), 'Asignacion.IdSolicitud': nuevoId() });
        libre.ocupado = true;
      } else logrado = 'Clasificado (sin sereno libre)';
    }
    console.log(`  SOS ${i + 1}: ${logrado} · ${ciudadano.nombre}`);
  } catch (error) {
    console.log(`  SOS ${i + 1}: se creó, pero no llegó a «${estado}»: ${error.message}`);
  }
}

console.log(`Listo: ${creados} de ${reportes.length} reportes en su estado y ${planSos.length} SOS. Serenos con caso: ${tokensSerenos.filter((s) => s.ocupado).length} de ${tokensSerenos.length}.`);
// Con CAD_MOVER=1 sigue con mover-serenos.mjs en el mismo proceso: usa las mismas cuentas sin volver a pedirlas.
if (process.env.CAD_MOVER === '1' && serenos.length) {
  process.env.CAD_CENTRO ??= `${centro.latitud},${centro.longitud}`;
  await import('./mover-serenos.mjs');
} else {
  console.log('Para que los serenos se muevan solos: node herramientas/demo/mover-serenos.mjs (o CAD_MOVER=1 con este guion).');
}

// El identificador del sereno: el panel lo muestra en la lista de serenos de la central (data-sereno con su nombre).
async function idSereno(sereno) {
  if (sereno.id) return sereno.id;
  const { html } = await panel.obtener('/panel/central?handler=Serenos');
  const filas = [...html.matchAll(/data-sereno="([0-9a-f-]{36})" data-nombre="([^"]+)"[^>]*data-disponible="true"/g)];
  // Se elige al disponible más cercano a la posición que se le dio (los nombres no se conocen desde la API).
  const posiciones = [...html.matchAll(/data-sereno="([0-9a-f-]{36})"[^>]*data-lat="([-0-9.]+)" data-lon="([-0-9.]+)"/g)]
    .map((m) => ({ id: m[1], latitud: Number(m[2]), longitud: Number(m[3]) }));
  const propio = posiciones.find((p) => Math.abs(p.latitud - sereno.punto.latitud) < 1e-6 && Math.abs(p.longitud - sereno.punto.longitud) < 1e-6);
  sereno.id = propio?.id ?? filas[0]?.[1];
  if (!sereno.id) throw new Error('No se encontró un sereno disponible en la central.');
  return sereno.id;
}
