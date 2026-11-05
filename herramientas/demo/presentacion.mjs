// Datos frescos para la presentación en una sola orden (05/11/2026):
//   1. Carga el dataset de J. L. Bustamante y Rivero (unos 5000 casos; al terminar quedan 5 SOS y 20 reportes abiertos en
//      distintas etapas), con el periodo terminando ahora. Vacía la base de datos antes (conserva el administrador).
//   2. Deja a los serenos en servicio moviéndose solos desde su última posición: patrullan y, si tienen un caso, van hacia él,
//      marcan «En camino» y, al llegar, «En atención». Sigue hasta Ctrl+C.
//
// Local (base de los user-secrets del servidor; el servidor en http://localhost:5220):
//   node herramientas/demo/presentacion.mjs
// Render (Neon): la URL del servicio, el archivo con la conexión de Neon y su host repetido para confirmar que se vacía esa base:
//   node herramientas/demo/presentacion.mjs --url https://<servicio>.onrender.com --conexion ~/.cad/neon.env --confirmar <host-de-neon>
//
// Opciones: --solo-cargar (sin mover a los serenos) · --solo-mover (sin recargar; usa la lista de la última carga)
//           --credenciales <archivo> (por omisión ~/.cad/cargador-credenciales.txt)
// La contraseña del personal se lee del archivo de credenciales del cargador y solo pasa al proceso que mueve a los serenos:
// nunca se escribe en la consola.
import { spawn } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const raiz = resolve(aqui, '..', '..');
const args = process.argv.slice(2);
const opcion = (nombre) => {
  const i = args.indexOf(`--${nombre}`);
  return i < 0 ? null : args[i + 1] && !args[i + 1].startsWith('--') ? args[i + 1] : '';
};
const casa = (ruta) => (ruta?.startsWith('~') ? join(homedir(), ruta.slice(1)) : ruta);

const url = (opcion('url') || process.env.CAD_URL || 'http://localhost:5220').replace(/\/+$/, '');
const conexion = casa(opcion('conexion'));
const confirmar = opcion('confirmar');
const rutaCredenciales = casa(opcion('credenciales')) || join(homedir(), '.cad', 'cargador-credenciales.txt');
const rutaServicio = join(homedir(), '.cad', 'serenos-en-servicio.json');
const CENTRO_JLBYR = '-16.4316,-71.5290';

function ejecutar(comando, argumentos, entorno = {}) {
  return new Promise((resolver, rechazar) => {
    const proceso = spawn(comando, argumentos, { cwd: raiz, stdio: 'inherit', env: { ...process.env, ...entorno } });
    proceso.on('error', rechazar);
    proceso.on('exit', (codigo) => (codigo === 0 ? resolver() : rechazar(new Error(`${comando} terminó con el código ${codigo}`))));
  });
}

// Render duerme el servicio gratuito: se despierta antes de cargar para que la central esté lista al terminar.
async function despertar() {
  const limite = Date.now() + 180_000;
  process.stdout.write(`Comprobando el servidor ${url}…`);
  while (Date.now() < limite) {
    try {
      const respuesta = await fetch(`${url}/salud`, { signal: AbortSignal.timeout(20_000) });
      if (respuesta.ok) { console.log(' responde.'); return; }
    } catch { /* aún arrancando */ }
    process.stdout.write('.');
    await new Promise((r) => setTimeout(r, 5000));
  }
  throw new Error(`El servidor ${url} no responde en /salud. ¿Está arrancado?`);
}

try {
  const inicio = Date.now();
  await despertar();

  if (!args.includes('--solo-mover')) {
    console.log('\n1/2 · Cargando el dataset (vacía la base de datos y la vuelve a llenar)…');
    const extra = [...(conexion ? ['--conexion', conexion] : []), ...(confirmar ? ['--confirmar', confirmar] : [])];
    await ejecutar('dotnet', ['run', '--project', 'herramientas/dataset-jlbyr/cargador', '-c', 'Release', '--',
      ...extra, '--credenciales', rutaCredenciales, '--serenos-en-servicio', rutaServicio]);
    console.log(`Carga terminada en ${Math.round((Date.now() - inicio) / 1000)} s.`);
  }
  if (args.includes('--solo-cargar')) process.exit(0);

  if (!existsSync(rutaServicio)) throw new Error(`No está la lista de serenos en servicio (${rutaServicio}): carga primero.`);
  const enServicio = JSON.parse(readFileSync(rutaServicio, 'utf8'));
  const clave = readFileSync(rutaCredenciales, 'utf8').match(/^contrasena_personal=(.+)$/m)?.[1]?.trim();
  if (!clave) throw new Error(`No encontré la contraseña del personal en ${rutaCredenciales}.`);
  console.log(`\n2/2 · Moviendo a los ${enServicio.length} serenos en servicio (Ctrl+C para detenerlos)…`);
  await ejecutar('node', [join(aqui, 'mover-serenos.mjs')], {
    CAD_URL: url,
    CAD_SERENOS: enServicio.map((s) => `${s.correo}:${clave}`).join(';'),
    CAD_INICIOS: rutaServicio,
    CAD_CENTRO: process.env.CAD_CENTRO ?? CENTRO_JLBYR,
    CAD_AVANZAR: process.env.CAD_AVANZAR ?? '1',
  });
} catch (error) {
  console.error(`\nNo se completó: ${error.message}`);
  process.exit(1);
}
