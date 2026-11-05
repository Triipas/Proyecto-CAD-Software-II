// Último caso llegado y clic en un hueco del mapa (central, 04/11/2026), contra el servidor y la base reales.
// - El reporte por revisar que llega en vivo desde la app (Recibido, en los casos en curso del mapa, no en la cola) se enfoca
//   y lleva el sonar igual que un SOS; el siguiente que llega lo releva y abrirlo lo apaga.
// - Un clic en un hueco del mapa suelta el caso elegido (SOS o reporte); arrastrar o acercar con doble clic no lo sueltan.
// Estas pruebas crean casos nuevos: corren en su propio proyecto («llegadas»), después de las capturas (playwright.config.js).
import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { LIMA, sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';
import { api, token } from '../soporte/semilla.js';

const ahora = () => new Date().toISOString();
const cerca = (dLat, dLon) => ({ latitud: LIMA.latitud + dLat, longitud: LIMA.longitud + dLon });

// Un ciudadano propio de estas pruebas, para no tocar los casos del escenario sembrado.
async function ciudadanoNuevo() {
  const correo = `ciro.${randomUUID().slice(0, 8)}@correo.test`;
  const clave = `Clave-${randomUUID().slice(0, 8)}9`;
  await api('/api/cuentas/registro', { cuerpo: { nombres: 'Ciro', apellidos: 'Ficticio', correo, telefono: '900000009', contrasena: clave } });
  return token(correo, clave);
}

async function enviarReporte(ciudadano, punto, descripcion) {
  const categorias = await api('/api/reportes/categorias', { metodo: 'GET', token: ciudadano });
  return api('/api/reportes', {
    token: ciudadano,
    cuerpo: { idSolicitud: randomUUID(), categoriaId: categorias[0].id, descripcion, ocurridoAproximadamenteEn: ahora(), ubicacion: { fuente: 'Mapa', ...punto, precisionMetros: null, referencia: null } },
  });
}

const enviarSos = (ciudadano, punto) =>
  api('/api/sos', { token: ciudadano, cuerpo: { idSolicitud: randomUUID(), activadoEn: ahora(), ubicacion: { ...punto, precisionMetros: 5, obtenidaEn: ahora() } } });

async function abrirCentral(page) {
  await prepararPagina(page);
  await abrir(page, '/panel/central');
  await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });
}

// Distancia en píxeles del punto al centro de la franja del mapa que no tapan los paneles.
async function desvioDelCentroLibre(page, { latitud, longitud }) {
  return page.evaluate(([lat, lon]) => {
    const { mapa } = window.cadCentral;
    const ancho = mapa.getContainer().clientWidth;
    const cola = document.getElementById('panel-cola').getBoundingClientRect();
    const serenos = document.getElementById('panel-serenos').getBoundingClientRect();
    const caja = mapa.getContainer().getBoundingClientRect();
    const izquierda = cola.right - caja.left;
    const derecha = Math.min(ancho, serenos.left - caja.left);
    const p = mapa.latLngToContainerPoint([lat, lon]);
    return Math.hypot(p.x - (izquierda + derecha) / 2, p.y - mapa.getContainer().clientHeight / 2);
  }, [latitud, longitud]);
}

// Un punto del mapa sin marcadores, globos ni controles (lejos de cualquier marcador), dentro de la franja libre.
async function huecoDelMapa(page) {
  return page.evaluate(() => {
    const mapa = window.cadCentral.mapa.getContainer();
    const caja = mapa.getBoundingClientRect();
    const cola = document.getElementById('panel-cola').getBoundingClientRect();
    const serenos = document.getElementById('panel-serenos').getBoundingClientRect();
    const ocupados = [...mapa.querySelectorAll('.leaflet-marker-icon, .leaflet-popup, .leaflet-control, .leaflet-tooltip, path')]
      .map((el) => el.getBoundingClientRect());
    for (let y = caja.top + 120; y < caja.bottom - 120; y += 30) {
      for (let x = cola.right + 40; x < serenos.left - 40; x += 30) {
        if (ocupados.some((r) => x > r.left - 40 && x < r.right + 40 && y > r.top - 40 && y < r.bottom + 40)) continue;
        const el = document.elementFromPoint(x, y);
        if (el && mapa.contains(el) && (el.classList.contains('leaflet-tile') || el === mapa)) return { x, y };
      }
    }
    return null;
  });
}

test.describe('Central: último caso llegado', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'no-preference' });

  test('El reporte por revisar que llega desde la app se enfoca y lleva el sonar, como un SOS', async ({ page }, info) => {
    await abrirCentral(page);
    const ciudadano = await ciudadanoNuevo();
    // Lejos del encuadre inicial: si el mapa no se enfocara en él, el reporte quedaría fuera de la vista.
    const puntoReporte = cerca(0.03, 0.025);
    await enviarReporte(ciudadano, puntoReporte, 'Puerta de un local forzada durante la madrugada (dato ficticio).');

    const sonar = page.locator('.sonar-llegada');
    await expect(sonar).toHaveCount(1, { timeout: 10_000 });
    await expect(sonar).toHaveClass(/sonar-llegada--reporte/);
    // El marcador destacado es el del reporte por revisar (borde discontinuo), entre los casos en curso del mapa.
    const destacado = page.locator('.marcador--ultimo');
    await expect(destacado).toHaveCount(1);
    await expect(destacado).toHaveClass(/marcador-caso--revision/);
    await expect(destacado).toHaveAttribute('title', /Reporte · Recibido/);
    await expect.poll(() => desvioDelCentroLibre(page, puntoReporte), { timeout: 5_000 }).toBeLessThan(12);
    expect(await page.evaluate(() => window.cadCentral.mapa.getZoom())).toBe(17);
    await page.screenshot({ path: info.outputPath('reporte-llegado.png') });

    // Un SOS que llega después releva al reporte: sigue habiendo un solo sonar, ahora sobre el SOS.
    const puntoSos = cerca(-0.025, -0.03);
    await enviarSos(ciudadano, puntoSos);
    await expect(sonar).toHaveCount(1, { timeout: 10_000 });
    await expect(sonar).not.toHaveClass(/sonar-llegada--reporte/);
    await expect(page.locator('.marcador--ultimo')).toHaveClass(/marcador-sos/);
    await expect.poll(() => desvioDelCentroLibre(page, puntoSos), { timeout: 5_000 }).toBeLessThan(12);

    // Un nuevo reporte vuelve a relevarlo; al abrirlo el operador (clic en su marcador), el sonar se apaga.
    const otroPunto = cerca(0.02, -0.02);
    await enviarReporte(ciudadano, otroPunto, 'Vehículo abandonado con las lunas rotas (dato ficticio).');
    await expect(sonar).toHaveClass(/sonar-llegada--reporte/, { timeout: 10_000 });
    await expect.poll(() => desvioDelCentroLibre(page, otroPunto), { timeout: 5_000 }).toBeLessThan(12);
    await page.locator('.marcador--ultimo').click();
    await expect(sonar).toHaveCount(0);
    await expect(page.locator('.marcador--ultimo')).toHaveCount(0);
  });

  test('Sin movimiento, el reporte por revisar llegado queda marcado con un anillo fijo', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await abrirCentral(page);
    const ciudadano = await ciudadanoNuevo();
    await enviarReporte(ciudadano, cerca(-0.02, 0.02), 'Basura acumulada que bloquea la vereda (dato ficticio).');
    const anillo = page.locator('.sonar-llegada--reporte span').first();
    await expect(anillo).toBeAttached({ timeout: 10_000 });
    expect(await anillo.evaluate((el) => getComputedStyle(el).animationName)).toBe('none');
    expect(Number(await anillo.evaluate((el) => getComputedStyle(el).opacity))).toBeGreaterThan(0);
  });
});

test.describe('Central: clic en un hueco del mapa', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'reduce' });

  test('Suelta el SOS elegido, pero arrastrar o acercar con doble clic no lo sueltan', async ({ page }, info) => {
    await abrirCentral(page);
    const elegir = page.locator('.central__urgente [data-seleccionar-sos]').first();
    await elegir.click();
    await expect(elegir).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.marcador--elegido')).toHaveCount(1);
    await expect(page.locator('path.linea-despacho')).toHaveCount(1);
    await page.evaluate(() => window.cadCentral.mapa.closePopup());

    // Arrastrar el mapa no suelta el caso.
    let hueco = await huecoDelMapa(page);
    expect(hueco).not.toBeNull();
    await page.mouse.move(hueco.x, hueco.y);
    await page.mouse.down();
    await page.mouse.move(hueco.x + 80, hueco.y + 40, { steps: 8 });
    await page.mouse.up();
    await page.waitForTimeout(400);
    await expect(elegir).toHaveAttribute('aria-pressed', 'true');

    // El doble clic acerca el mapa y deja el caso elegido.
    hueco = await huecoDelMapa(page);
    const zoom = await page.evaluate(() => window.cadCentral.mapa.getZoom());
    await page.mouse.dblclick(hueco.x, hueco.y);
    await expect.poll(() => page.evaluate(() => window.cadCentral.mapa.getZoom())).toBeGreaterThan(zoom);
    await page.waitForTimeout(400);
    await expect(elegir).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.marcador--elegido')).toHaveCount(1);

    // Un clic en el hueco lo suelta: sin resaltado, sin línea de despacho y la barra de asignación vacía.
    hueco = await huecoDelMapa(page);
    await page.mouse.click(hueco.x, hueco.y);
    await expect(elegir).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('.marcador--elegido')).toHaveCount(0);
    await expect(page.locator('path.linea-despacho')).toHaveCount(0);
    await expect(page.locator('.sos--seleccionado')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath('hueco-suelta.png') });
  });

  test('Suelta el reporte por revisar elegido desde su marcador', async ({ page }) => {
    await abrirCentral(page);
    // El reporte en revisión del escenario se elige desde su marcador (no está en la cola).
    const marcador = page.locator('.marcador-caso--revision[title*="En revisión"]').first();
    await page.evaluate(() => {
      const caso = document.querySelector('[data-caso-mapa][data-estado="En revisión"]');
      window.cadCentral.mapa.setView([Number(caso.dataset.lat), Number(caso.dataset.lon)], 16, { animate: false });
    });
    await marcador.click();
    await expect(page.locator('.marcador--elegido')).toHaveCount(1);
    await page.evaluate(() => window.cadCentral.mapa.closePopup());
    const hueco = await huecoDelMapa(page);
    await page.mouse.click(hueco.x, hueco.y);
    await expect(page.locator('.marcador--elegido')).toHaveCount(0);
  });
});
