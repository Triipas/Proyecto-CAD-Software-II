// Servidor estático mínimo para ver los prototipos: sirve la raíz del repositorio para que
// los prototipos usen los tokens y fuentes reales de src/Cad.Server/wwwroot.
// Uso: node docs/rediseno/prototipos/servir.mjs  →  http://localhost:5250/docs/rediseno/prototipos/
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const RAIZ = resolve(fileURLToPath(new URL('.', import.meta.url)), '..', '..', '..');
const PUERTO = Number(process.env.PUERTO ?? 5250);
const TIPOS = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8', '.svg': 'image/svg+xml', '.woff2': 'font/woff2', '.png': 'image/png', '.json': 'application/json' };

createServer(async (pet, res) => {
  let ruta = decodeURIComponent(new URL(pet.url, 'http://x').pathname);
  if (ruta === '/') ruta = '/docs/rediseno/prototipos/';
  const archivo = normalize(join(RAIZ, ruta));
  if (!archivo.startsWith(RAIZ)) { res.writeHead(403).end(); return; }
  try {
    const destino = (await stat(archivo)).isDirectory() ? join(archivo, 'index.html') : archivo;
    const cuerpo = await readFile(destino);
    res.writeHead(200, { 'Content-Type': TIPOS[extname(destino)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' }).end(cuerpo);
  } catch {
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' }).end('No encontrado');
  }
}).listen(PUERTO, '127.0.0.1', () => console.log(`Prototipos en http://localhost:${PUERTO}/docs/rediseno/prototipos/`));
