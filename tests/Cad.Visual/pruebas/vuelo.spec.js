// Vuelo al caso en la central (rediseño CAD v2). Desde el 24/10/2026 elegir el caso es la única acción del mapa (reemplazó
// a «Ver en el mapa» y «Elegir sereno»): desde la cola encuadra el caso con su sereno sugerido sin dejarlos bajo los paneles
// flotantes; desde su marcador lo centra en la franja libre sin cambiar el zoom.
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

  test('Elegir el caso en la cola lo encuadra con su sereno sugerido y lo deja a la vista', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    const elegir = page.locator('.central__urgente [data-seleccionar-sos]').first();
    await elegir.click();
    await expect(elegir).toHaveAttribute('aria-pressed', 'true');
    // Al aterrizar aparecen los anillos de llegada y se abre el globo del caso; los serenos se abren para asignar.
    await expect(page.locator('.llegada')).toBeAttached({ timeout: 5_000 });
    const globo = page.locator('.leaflet-popup');
    await expect(globo).toContainText('Pendiente');
    await expect(globo).toContainText('Sugerido'); // el globo nombra al sereno sugerido
    await expect(globo.getByRole('link', { name: 'Abrir el SOS' })).toBeVisible();
    await expect(page.locator('#panel-serenos')).not.toHaveClass(/panel-flotante--plegado/);
    expect(await page.evaluate(() => window.cadCentral.mapa.getZoom())).toBeLessThanOrEqual(17);

    const { izquierda, derecha } = await franjaLibre(page);
    const marcador = await page.locator('.marcador-sos.marcador--elegido').boundingBox();
    const centro = marcador.x + marcador.width / 2;
    expect(centro).toBeGreaterThan(izquierda);
    expect(centro).toBeLessThan(derecha);
    const linea = await page.locator('path.linea-despacho').boundingBox();
    expect(linea.x).toBeGreaterThanOrEqual(izquierda);
    expect(linea.x + linea.width).toBeLessThanOrEqual(derecha);
    await page.screenshot({ path: info.outputPath('vuelo-encuadre.png') });
    await expect(page.locator('.llegada')).toHaveCount(0, { timeout: 3_000 }); // los anillos se retiran solos
  });

  test('Elegir el caso desde su marcador lo centra en la franja libre sin cambiar el zoom', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    // Primero se trae el caso a la vista desde la cola; luego el mapa se corre para dejarlo junto al borde de la cola.
    await page.locator('.central__urgente [data-seleccionar-sos]').first().click();
    await expect(page.locator('.llegada')).toBeAttached({ timeout: 5_000 }); // aterrizó
    await expect(page.locator('.llegada')).toHaveCount(0, { timeout: 3_000 });
    const { izquierda, derecha } = await franjaLibre(page);
    const sos = page.locator('.marcador-sos').first();
    const antes = await sos.boundingBox();
    // Sin el globo abierto: un clic en el marcador con su globo abierto lo cerraría.
    await page.evaluate((dx) => { window.cadCentral.mapa.closePopup(); window.cadCentral.mapa.panBy([dx, 0], { animate: false }); }, antes.x + antes.width / 2 - (izquierda + 40));
    const zoom = await page.evaluate(() => window.cadCentral.mapa.getZoom());

    await sos.click();
    await expect(page.locator('.leaflet-popup').getByRole('link', { name: 'Abrir el SOS' })).toBeVisible();
    await expect.poll(async () => {
      const caja = await sos.boundingBox();
      return Math.abs(caja.x + caja.width / 2 - (izquierda + derecha) / 2);
    }, { timeout: 3_000 }).toBeLessThan(8);
    expect(await page.evaluate(() => window.cadCentral.mapa.getZoom())).toBe(zoom);
    await page.screenshot({ path: info.outputPath('vuelo-centrado.png') });
  });
});
