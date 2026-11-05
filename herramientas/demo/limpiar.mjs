// Deja la central sin SOS ni reportes (05/11/2026) ejecutando limpiar-casos.sql en una sola transacción.
// La cadena de conexión de PostgreSQL (la de Neon en Render, formato postgresql://...) se pide en la terminal sin mostrarla,
// o va en CAD_BD. Antes de borrar muestra cuántos casos hay y pide escribir BORRAR. Con CAD_PROBAR=1 lo hace todo y
// deshace la transacción al final (para comprobar el guion sin tocar nada).
//
//   node herramientas/demo/limpiar.mjs
//
// Usa el paquete «pg» de tests/Cad.Visual (npm ci en esa carpeta si no está instalado).
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { createInterface } from 'node:readline/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { preguntar } from './comun.mjs';

const aqui = dirname(fileURLToPath(import.meta.url));
const { Client } = createRequire(join(aqui, '..', '..', 'tests', 'Cad.Visual', 'package.json'))('pg');
const probar = process.env.CAD_PROBAR === '1';

const cadena = process.env.CAD_BD ?? await preguntar('Cadena de conexión de PostgreSQL (postgresql://...): ', true);
if (!cadena) {
  console.error('Falta la cadena de conexión.');
  process.exit(1);
}
const local = /@(localhost|127\.0\.0\.1)[:/]/.test(cadena);
const cliente = new Client({ connectionString: cadena, ssl: local ? false : { rejectUnauthorized: false } });
await cliente.connect();

const cuenta = async () => (await cliente.query(`SELECT "Origen" AS origen, count(*)::int AS n FROM "Casos" GROUP BY 1 ORDER BY 1`)).rows;
const antes = await cuenta();
console.log(`Casos ahora: ${antes.map((f) => `${f.origen} ${f.n}`).join(' · ') || 'ninguno'}`);

if (!probar && !process.env.CAD_CONFIRMAR) {
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  const respuesta = (await rl.question('Se borrarán TODOS los SOS y reportes. Escribe BORRAR para continuar: ')).trim();
  rl.close();
  if (respuesta !== 'BORRAR') {
    console.log('Cancelado: no se borró nada.');
    await cliente.end();
    process.exit(0);
  }
}

// Las órdenes del archivo, una a una dentro de una transacción; se salta la tabla que esa base aún no tenga (migración
// pendiente). Con CAD_PROBAR=1 se deshace al final.
const ordenes = readFileSync(join(aqui, 'limpiar-casos.sql'), 'utf8')
  .split(/\r?\n/).filter((l) => /^(DELETE|UPDATE)\b/.test(l));
try {
  await cliente.query('BEGIN');
  for (const orden of ordenes) {
    const tabla = orden.match(/"([^"]+)"/)[1];
    if (!(await cliente.query('SELECT to_regclass($1) AS t', [`"${tabla}"`])).rows[0].t) {
      console.log(`  (no existe «${tabla}» en esta base: se salta)`);
      continue;
    }
    const { rowCount } = await cliente.query(orden);
    if (rowCount) console.log(`  ${tabla}: ${rowCount}`);
  }
  if (!probar) await cliente.query('COMMIT');
  const despues = await cuenta();
  if (probar) await cliente.query('ROLLBACK');
  console.log(probar
    ? `Prueba: habría quedado ${despues.length ? JSON.stringify(despues) : 'sin casos'}; se deshizo todo.`
    : 'Listo: la central quedó sin SOS ni reportes y los serenos fuera de servicio. Recarga la central.');
} catch (error) {
  await cliente.query('ROLLBACK').catch(() => {});
  console.error(`No se borró nada (se deshizo la transacción): ${error.message}`);
  process.exitCode = 1;
} finally {
  await cliente.end();
}
