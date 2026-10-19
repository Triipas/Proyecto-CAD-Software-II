// Vuelo al caso en la central (rediseño CAD v2): «Ver en el mapa» lleva el mapa al punto casi al zoom máximo,
// sin dejarlo bajo los paneles flotantes, y «Elegir sereno» encuadra el caso con el sereno más cercano.
// Corre con el movimiento activado (el resto de la revisión usa movimiento reducido).
import { test, expect } from '@playwright/test';
import { sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';

test.describe('Central: vuelo al caso', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'no-preference' });

  // Franja del mapa que no tapan la cola (izquierda) ni los serenos (derecha).
  async function franjaLibre(page) {
    const cola = await page.locator('#panel-cola').boundingBox();
    const serenos = await page.locator('#panel-serenos').boundingBox();
    return { izquierda: cola.x + cola.width, derecha: serenos.x };
  }

  test('«Ver en el mapa» vuela al SOS con zoom 18 y lo deja a la vista', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    await page.locator('.central__urgente [data-ver-en-mapa]').first().click();
    // Al aterrizar aparecen los anillos de llegada y se abre el globo del caso.
    await expect(page.locator('.llegada')).toBeAttached({ timeout: 5_000 });
    await expect(page.locator('.leaflet-popup')).toContainText('SOS · Pendiente');
    expect(await page.evaluate(() => window.cadCentral.mapa.getZoom())).toBe(18);

    const marcador = await page.locator('.marcador-sos.marcador--elegido').boundingBox();
    const { izquierda, derecha } = await franjaLibre(page);
    const centro = marcador.x + marcador.width / 2;
    expect(centro).toBeGreaterThan(izquierda);
    expect(centro).toBeLessThan(derecha);
    await page.screenshot({ path: info.outputPath('vuelo-aterrizaje.png') });
    await expect(page.locator('.llegada')).toHaveCount(0, { timeout: 3_000 }); // los anillos se retiran solos
  });

  test('«Elegir sereno» encuadra el caso y el sereno más cercano', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    await page.locator('.central__urgente [data-seleccionar-sos]').first().click();
    await expect(page.locator('.llegada')).toBeAttached({ timeout: 5_000 });
    const linea = await page.locator('path.linea-despacho').boundingBox();
    const { izquierda, derecha } = await franjaLibre(page);
    expect(linea.x).toBeGreaterThanOrEqual(izquierda);
    expect(linea.x + linea.width).toBeLessThanOrEqual(derecha);
    await page.screenshot({ path: info.outputPath('vuelo-encuadre.png') });
  });
});
