// Movimiento de la central (fase 1, 26/10/2026): las listas y los marcadores se actualizan en su sitio. No cambia datos: simula
// llegadas y salidas tocando el DOM antes de resincronizar con el servidor.
import { test, expect } from '@playwright/test';
import { sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';

const REGIONES = '[data-region-lista], [data-region-reportes], [data-region-serenos]';

// Animaciones vivas dentro de las listas de la central (no cuenta el pulso de los marcadores ni el sonar).
const animacionesEnListas = (pagina) => pagina.evaluate((regiones) =>
  document.getAnimations().filter((a) => a.playState === 'running' && a.effect?.target?.closest?.(regiones)).length, REGIONES);

async function abrirCentral(page) {
  await prepararPagina(page);
  await abrir(page, '/panel/central');
  await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });
}

test.describe('Central: movimiento', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'no-preference' });

  test('resincronizar sin cambios conserva tarjetas y marcadores, y no mueve nada', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await abrirCentral(page);
    await page.evaluate(() => {
      // Una propiedad, no un atributo: la actualización quita los atributos que el servidor no envía.
      for (const el of document.querySelectorAll('[data-sos], [data-sereno], .leaflet-marker-icon')) el.marcaPrueba = true;
    });
    await page.evaluate(async () => { await window.cadCentral.refrescar(); await window.cadCentral.refrescarSerenos(); });

    const sinMarca = await page.evaluate(() =>
      [...document.querySelectorAll('[data-sos], [data-sereno], .leaflet-marker-icon')].filter((el) => !el.marcaPrueba).map((el) => el.className));
    expect(sinMarca).toEqual([]);
    expect(await animacionesEnListas(page)).toBe(0);
  });

  test('lo que llega entra, lo que se va se desvanece sin quedar como caso', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await abrirCentral(page);
    const quitado = await page.evaluate(() => {
      const reporte = document.querySelector('[data-region-reportes] [data-sos]');
      reporte.remove();
      const falso = document.createElement('li');
      Object.assign(falso, { id: 'sos-falso', className: 'sos sos--reporte', textContent: 'Caso que ya no está' });
      falso.dataset.clave = 'falso';
      falso.dataset.sos = 'falso';
      document.querySelector('[data-region-reportes] .lista-sos').append(falso);
      return reporte.dataset.sos;
    });
    await page.evaluate(() => window.cadCentral.refrescar());

    // El reporte vuelve (llega) con su entrada y su anillo; el falso se fue y su copia no es un caso.
    const llegado = page.locator(`#sos-${quitado}`);
    await expect(llegado).toBeVisible();
    await expect(llegado).toHaveClass(/sos--llegada/);
    expect(await page.evaluate(() => document.getElementById('sos-falso'))).toBeNull();
    expect(await page.locator('[data-region-reportes] [aria-hidden="true"][inert]').count()).toBe(1);
    expect(await animacionesEnListas(page)).toBeGreaterThan(0);

    // Al terminar no queda rastro.
    await expect(page.locator('[data-region-reportes] [aria-hidden="true"][inert]')).toHaveCount(0);
    await expect(llegado).not.toHaveClass(/sos--llegada/, { timeout: 3_000 });
  });

  test('la resincronización no le quita el foco ni la selección al operador', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await abrirCentral(page);
    const boton = page.locator('.central__urgente [data-seleccionar-sos]').first();
    await boton.click();
    await boton.focus();
    await page.evaluate(async () => { await window.cadCentral.refrescar(); await window.cadCentral.refrescarSerenos(); });

    await expect(boton).toBeFocused();
    await expect(boton).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.sereno--sugerido [data-distancia]')).toHaveCount(1);
    await expect(page.locator('[data-asignar-sereno]:visible').first()).toBeVisible();
  });
});

test.describe('Central: movimiento reducido', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'reduce' });

  test('lo que llega aparece sin animación', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await abrirCentral(page);
    await page.evaluate(() => document.querySelector('[data-region-reportes] [data-sos]').remove());
    await page.evaluate(() => window.cadCentral.refrescar());

    expect(await animacionesEnListas(page)).toBe(0);
    expect(await page.locator('[data-region-reportes] [aria-hidden="true"][inert]').count()).toBe(0);
  });
});
