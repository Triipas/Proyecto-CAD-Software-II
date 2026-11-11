import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const resources = resolve(import.meta.dirname, '../../../src/Cad.Movil/Resources/Raw/mapa');
const yo = { lat: -16.425, lon: -71.515 };
const destino = { lat: -16.418, lon: -71.52, tipo: 'sos', precision: 8 };
const ruta = [[yo.lat, yo.lon], [-16.421, -71.515], [destino.lat, destino.lon]];

async function abrir(page, oculto = false) {
  await page.route('https://tile.openstreetmap.org/**', route => route.abort());
  const html = readFileSync(resolve(resources, 'despacho.html'), 'utf8')
    .replace('/*LEAFLET_CSS*/', () => readFileSync(resolve(resources, 'leaflet.css'), 'utf8'))
    .replace('/*LEAFLET_JS*/', () => readFileSync(resolve(resources, 'leaflet.js'), 'utf8') +
      '\n;const original=L.map;L.map=(...args)=>window.mapaPrueba=original(...args);')
    .replace('/*CONFIG*/', JSON.stringify({ lat: -16.42, lon: -71.52, zoom: 13,
      teselas: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png', reducido: false }))
    .replace('<body>', oculto ? '<body style="display:none">' : '<body>');
  await page.setContent(html);
}

test('El despacho que llega con el mapa oculto se encuadra al aparecer', async ({ page }) => {
  await abrir(page, true);
  await page.evaluate(datos => window.cad.mostrar(datos), { yo, destino, ruta });
  await page.evaluate(() => { document.body.style.display = ''; window.cad.ajustar(); });
  await expect.poll(() => page.evaluate(puntos => puntos.every(p => window.mapaPrueba.getBounds().contains(p)), ruta)).toBe(true);
  await expect(page.locator('.pin--sos')).toBeVisible();
});

test('La ruta sigue visible cuando el WebView termina el encuadre sin animarlo', async ({ page }) => {
  await abrir(page);
  await page.evaluate(datos => window.cad.mostrar(datos), { yo, destino });
  // Leaflet salta directamente al encuadre en WebViews sin transformaciones 3D.
  await page.evaluate(datos => { L.Browser.any3d = false; window.cad.mostrar(datos); }, { yo, destino, ruta });
  const linea = page.locator('.leaflet-overlay-pane path[stroke="#1D4ED8"]');
  await expect(linea).toBeVisible();
  await expect.poll(() => linea.evaluate(el => getComputedStyle(el).strokeDasharray)).not.toMatch(/^0(?:px)?[, ]/);
});
