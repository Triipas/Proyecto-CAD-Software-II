// Ayudantes para que cada captura sea estable: sin teselas externas, sin horas ni identificadores variables.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { ESTADO } from './entorno.js';

const TESELA_VACIA = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=', 'base64');

export async function prepararPagina(pagina) {
  // Las teselas de OpenStreetMap cambian y dependen de la red: el mapa se revisa sin ellas.
  await pagina.route(/tile\.openstreetmap\.org/, (ruta) => ruta.fulfill({ contentType: 'image/png', body: TESELA_VACIA }));
}

export async function abrir(pagina, ruta) {
  await pagina.goto(ruta);
  await pagina.waitForLoadState('networkidle');
  await pagina.evaluate(() => document.fonts.ready);
}

// Sustituye los textos que cambian en cada ejecución (horas, fechas, duraciones, antigüedad,
// identificadores) por valores fijos del mismo largo aproximado, para comparar solo el diseño.
export async function normalizarTextoDinamico(pagina) {
  await pagina.evaluate(() => {
    const reglas = [
      [/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '00000000-0000-0000-0000-000000000000'],
      [/\b\d{2}\/\d{2}\/\d{4}\b/g, '17/10/2026'],
      [/\b\d{1,2}:\d{2}:\d{2}\b/g, '23:43:06'],
      [/\b\d{1,2}:\d{2}\b/g, '23:43'],
      [/\bhace \d+ (s|min)( \d+ s)?\b/g, 'hace 14 s'],
      [/\b\d+ min \d{2} s\b/g, '4 min 00 s'],
      [/\b\d+ s\b/g, '14 s'],
      [/-?\d{1,3}\.\d{5}, -?\d{1,3}\.\d{5}/g, '-12.04530, -77.03650'],
    ];
    const recorrido = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    for (let nodo = recorrido.nextNode(); nodo; nodo = recorrido.nextNode()) {
      let texto = nodo.nodeValue;
      for (const [patron, fijo] of reglas) texto = texto.replace(patron, fijo);
      if (texto !== nodo.nodeValue) nodo.nodeValue = texto;
    }
    for (const campo of document.querySelectorAll('input[type="date"]')) campo.value = '2026-10-17';
  });
}

export const capturaCompleta = { fullPage: true };

// Guarda el HTML renderizado, con sus hojas de estilo en línea, para el detector de impeccable (npm run detectar).
export async function guardarHtml(pagina, nombre) {
  const html = await pagina.evaluate(async () => {
    const copia = document.documentElement.cloneNode(true);
    copia.querySelectorAll('script').forEach((s) => s.remove());
    for (const enlace of copia.querySelectorAll('link[rel="stylesheet"]')) {
      const css = await (await fetch(enlace.href)).text();
      const estilo = document.createElement('style');
      estilo.textContent = css.replace(/url\((["']?)\.\.\//g, `url($1${location.origin}/`);
      enlace.replaceWith(estilo);
    }
    return '<!doctype html>\n' + copia.outerHTML;
  });
  const carpeta = join(ESTADO, 'html');
  mkdirSync(carpeta, { recursive: true });
  writeFileSync(join(carpeta, `${nombre}.html`), html);
}
