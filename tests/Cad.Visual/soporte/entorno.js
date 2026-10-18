// Constantes y rutas de la revisión visual. La base cad_visual es desechable: se borra y se recrea en cada ejecución.
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const RAIZ_VISUAL = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const RAIZ_REPO = resolve(RAIZ_VISUAL, '..', '..');
export const ESTADO = join(RAIZ_VISUAL, '.estado');
export const ARCHIVO_SEMILLA = join(ESTADO, 'semilla.json');
export const sesion = (rol) => join(ESTADO, `sesion-${rol}.json`);

export const BASE_DATOS = 'cad_visual';
export const PUERTO = Number(process.env.CAD_VISUAL_PUERTO ?? 5240);
export const URL_BASE = `http://localhost:${PUERTO}`;

// Ubicación simulada en el centro de Lima, como en el resto de demostraciones.
export const LIMA = { latitud: -12.0453, longitud: -77.0365 };

// Credenciales de PostgreSQL: CAD_VISUAL_CONEXION (CI) o el .env de la raíz (el mismo de docker compose y las pruebas).
export function conexionPostgres(baseDatos = BASE_DATOS) {
  const variable = process.env.CAD_VISUAL_CONEXION;
  if (variable) return { connectionString: variable.replace(/Database=[^;]*/i, `Database=${baseDatos}`) };

  const archivo = join(RAIZ_REPO, '.env');
  if (!existsSync(archivo))
    throw new Error('No se encontró .env en la raíz del repositorio. Copia .env.example a .env y levanta PostgreSQL con `docker compose up -d`.');
  const valores = Object.fromEntries(
    readFileSync(archivo, 'utf8').split(/\r?\n/)
      .map((linea) => linea.trim())
      .filter((linea) => linea && !linea.startsWith('#') && linea.includes('='))
      .map((linea) => [linea.slice(0, linea.indexOf('=')).trim(), linea.slice(linea.indexOf('=') + 1).trim()]),
  );
  if (!valores.POSTGRES_PASSWORD) throw new Error('Define POSTGRES_PASSWORD en .env.');
  return {
    host: 'localhost',
    port: Number(valores.POSTGRES_PORT ?? 5432),
    user: valores.POSTGRES_USER ?? 'cad',
    password: valores.POSTGRES_PASSWORD,
    database: baseDatos,
  };
}

// Cadena de conexión en formato Npgsql para el servidor.
export function cadenaNpgsql() {
  const c = conexionPostgres();
  if (c.connectionString) return c.connectionString;
  return `Host=${c.host};Port=${c.port};Database=${c.database};Username=${c.user};Password=${c.password}`;
}

export function leerSemilla() {
  return JSON.parse(readFileSync(ARCHIVO_SEMILLA, 'utf8'));
}
