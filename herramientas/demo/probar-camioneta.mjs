// Vista local de los sprites reales del sistema. No usa cuentas ni modifica la base de datos.
// node herramientas/demo/probar-camioneta.mjs → http://localhost:5251
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const publico = resolve(aqui, '../../src/Cad.Server/wwwroot');
const tipos = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
createServer(async (solicitud, respuesta) => {
  try {
    const ruta = decodeURIComponent(new URL(solicitud.url, 'http://localhost').pathname);
    const archivo = ruta === '/' ? resolve(aqui, 'camioneta-preview.html') : resolve(publico, `.${ruta}`);
    if (ruta !== '/' && (!archivo.startsWith(publico + sep) || !/^\/(css|js|img|lib|fonts)\//.test(ruta))) {
      respuesta.writeHead(404).end(); return;
    }
    const contenido = await readFile(archivo);
    respuesta.writeHead(200, { 'Content-Type': tipos[extname(archivo)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    respuesta.end(contenido);
  } catch { respuesta.writeHead(404).end(); }
}).listen(5251, '127.0.0.1', () => console.log('Prueba de camioneta: http://localhost:5251 · Ctrl+C para cerrar'));
