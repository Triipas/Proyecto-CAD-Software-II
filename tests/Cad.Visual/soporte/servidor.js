// Arranca un servidor CAD aislado contra la base desechable cad_visual, con secretos generados en cada ejecución.
import { spawn, execFileSync } from 'node:child_process';
import { createWriteStream, mkdirSync } from 'node:fs';
import { randomBytes, randomUUID } from 'node:crypto';
import { join } from 'node:path';
import pg from 'pg';
import { BASE_DATOS, ESTADO, PUERTO, RAIZ_REPO, URL_BASE, cadenaNpgsql, conexionPostgres } from './entorno.js';

export async function recrearBaseDatos() {
  const cliente = new pg.Client(conexionPostgres('postgres'));
  await cliente.connect();
  try {
    // Protección: este código solo borra la base de la revisión visual.
    if (BASE_DATOS !== 'cad_visual') throw new Error(`Base inesperada: ${BASE_DATOS}`);
    await cliente.query(`DROP DATABASE IF EXISTS ${BASE_DATOS} WITH (FORCE)`);
    await cliente.query(`CREATE DATABASE ${BASE_DATOS}`);
  } finally {
    await cliente.end();
  }
}

export function compilarServidor() {
  execFileSync('dotnet', ['build', 'src/Cad.Server', '-v', 'q', '-nologo'], { cwd: RAIZ_REPO, stdio: 'inherit' });
}

// Secretos de prueba: se generan en cada ejecución y solo viven en .estado/ (ignorado por git).
export function generarSecretos() {
  return {
    jwt: randomBytes(32).toString('base64'),
    admin: { correo: `admin.visual@cad.test`, clave: `Visual-${randomUUID().slice(0, 8)}1` },
  };
}

export async function arrancarServidor(secretos) {
  mkdirSync(ESTADO, { recursive: true });
  const registro = createWriteStream(join(ESTADO, 'servidor.log'));
  const proyecto = join(RAIZ_REPO, 'src', 'Cad.Server');
  // Se ejecuta la DLL con el proyecto como raíz de contenido: wwwroot es la carpeta fuente,
  // así cada cambio de CSS se revisa sin recompilar. Development permite las cookies del panel por HTTP;
  // las variables de entorno de abajo tienen prioridad sobre los user-secrets, así que la base y los secretos son los de aquí.
  const proceso = spawn('dotnet', [join(proyecto, 'bin', 'Debug', 'net10.0', 'Cad.Server.dll'), '--urls', URL_BASE], {
    cwd: proyecto,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__Cad: cadenaNpgsql(),
      BaseDatos__MigrarAlIniciar: 'true',
      Jwt__ClaveFirma: secretos.jwt,
      Admin__Email: secretos.admin.correo,
      Admin__ClaveInicial: secretos.admin.clave,
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  proceso.stdout.pipe(registro);
  proceso.stderr.pipe(registro);

  const limite = Date.now() + 90_000;
  while (Date.now() < limite) {
    if (proceso.exitCode !== null) throw new Error(`El servidor terminó al arrancar (código ${proceso.exitCode}). Revisa .estado/servidor.log.`);
    try {
      if ((await fetch(`${URL_BASE}/salud/lista`)).ok) return proceso;
    } catch { /* aún no escucha */ }
    await new Promise((r) => setTimeout(r, 500));
  }
  proceso.kill();
  throw new Error(`El servidor no respondió en ${URL_BASE} en 90 s. Revisa .estado/servidor.log (¿puerto ${PUERTO} ocupado?).`);
}
