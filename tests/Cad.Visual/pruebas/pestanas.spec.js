// Pestañas de la bandeja de la central (05/11/2026), contra el servidor y la base reales: SOS, Reportes y En curso. La elegida
// decide qué casos se ven en la bandeja y en el mapa (los serenos, siempre); lo que llega a otra pestaña se avisa en ella.
// Crea casos nuevos: corre en el proyecto «llegadas», después de las capturas (playwright.config.js).
import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { LIMA, sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';
import { api, token } from '../soporte/semilla.js';

const ahora = () => new Date().toISOString();
const cerca = (dLat, dLon) => ({ latitud: LIMA.latitud + dLat, longitud: LIMA.longitud + dLon });

async function ciudadanoNuevo() {
  const correo = `tania.${randomUUID().slice(0, 8)}@correo.test`;
  const clave = `Clave-${randomUUID().slice(0, 8)}9`;
  await api('/api/cuentas/registro', { cuerpo: { nombres: 'Tania', apellidos: 'Ficticia', correo, telefono: '900000011', contrasena: clave } });
  return token(correo, clave);
}

async function enviarReporte(ciudadano, punto) {
  const categorias = await api('/api/reportes/categorias', { metodo: 'GET', token: ciudadano });
  return (await api('/api/reportes', {
    token: ciudadano,
    cuerpo: { idSolicitud: randomUUID(), categoriaId: categorias[0].id, descripcion: 'Cables caídos sobre la vereda (dato ficticio).', ocurridoAproximadamenteEn: ahora(), ubicacion: { fuente: 'Mapa', ...punto, precisionMetros: null, referencia: null } },
  })).id;
}

const enviarSos = async (ciudadano, punto) =>
  (await api('/api/sos', { token: ciudadano, cuerpo: { idSolicitud: randomUUID(), activadoEn: ahora(), ubicacion: { ...punto, precisionMetros: 5, obtenidaEn: ahora() } } })).id;

// Clases de los marcadores de casos que se ven en el mapa (sin los serenos).
const casosEnElMapa = (page) => page.evaluate(() =>
  [...document.querySelectorAll('.leaflet-marker-pane > :is(.marcador-sos, .marcador-reporte)')]
    .filter((el) => getComputedStyle(el).display !== 'none')
    .map((el) => [...el.classList].filter((c) => c.startsWith('marcador-')).join(' ')));

async function abrirCentral(page) {
  await prepararPagina(page);
  await abrir(page, '/panel/central');
  await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });
}

test.describe('Central: pestañas de la bandeja', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'reduce' });

  test('Cada pestaña muestra en la bandeja y en el mapa solo sus casos, y se recuerda al volver', async ({ page }, info) => {
    await abrirCentral(page);
    const pestana = (nombre) => page.getByRole('tab', { name: new RegExp(`^${nombre}`) });
    await expect(page.getByRole('tablist', { name: 'Qué casos ver' })).toBeVisible();

    // SOS (por defecto): la lista de SOS y solo SOS sin sereno en el mapa.
    await expect(pestana('SOS')).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('.central__urgente')).toBeVisible();
    await expect(page.locator('.central__revisar')).toBeHidden();
    await expect(page.locator('.central__asignados')).toBeHidden();
    const sos = await casosEnElMapa(page);
    expect(sos.length).toBeGreaterThan(0);
    expect(sos.every((c) => c.includes('marcador-sos') && !c.includes('en-curso'))).toBe(true);
    await expect(page.locator('.leaflet-marker-pane .marcador-sereno').first()).toBeVisible();
    await page.screenshot({ path: info.outputPath('pestana-sos.png') });

    // Reportes: por revisar y por asignar; en el mapa, solo reportes sin sereno.
    await pestana('Reportes').click();
    await expect(page.locator('.central__revisar')).toBeVisible();
    await expect(page.locator('.central__reportes')).toBeVisible();
    await expect(page.locator('.central__urgente')).toBeHidden();
    const reportes = await casosEnElMapa(page);
    expect(reportes.length).toBeGreaterThan(0);
    expect(reportes.every((c) => c.includes('marcador-reporte') && (!c.includes('en-curso') || c.includes('revision')))).toBe(true);
    await expect(page.locator('.leaflet-marker-pane .marcador-sereno').first()).toBeVisible();
    await page.screenshot({ path: info.outputPath('pestana-reportes.png') });

    // El globo de un reporte abierto en el mapa se cierra al pasar a otra pestaña (su marcador deja de verse).
    await page.evaluate(() => {
      const caso = document.querySelector('[data-caso-mapa][data-estado="En revisión"]');
      window.cadCentral.mapa.setView([Number(caso.dataset.lat), Number(caso.dataset.lon)], 16, { animate: false });
    });
    await page.locator('.leaflet-marker-pane .marcador-caso--revision').first().click();
    await expect(page.locator('.leaflet-popup')).toHaveCount(1);

    // En curso, con el teclado (flecha a la derecha): solo casos con sereno.
    await pestana('Reportes').focus();
    await pestana('Reportes').press('ArrowRight');
    await expect(page.locator('.leaflet-popup')).toHaveCount(0);
    await expect(pestana('En curso')).toHaveAttribute('aria-selected', 'true');
    await expect(pestana('En curso')).toBeFocused();
    await expect(page.locator('.central__asignados')).toBeVisible();
    expect((await casosEnElMapa(page)).every((c) => c.includes('en-curso') && !c.includes('revision'))).toBe(true);

    // La pestaña elegida se recuerda al volver a la central.
    await pestana('En curso').press('ArrowLeft');
    await expect(pestana('Reportes')).toHaveAttribute('aria-selected', 'true');
    await page.reload();
    await expect(pestana('Reportes')).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('.central__revisar')).toBeVisible();
  });

  test('Lo que llega a otra pestaña se avisa en ella; un SOS, sin otro caso entre manos, trae su pestaña', async ({ page }) => {
    await abrirCentral(page);
    const pestana = (nombre) => page.getByRole('tab', { name: new RegExp(`^${nombre}`) });
    const ciudadano = await ciudadanoNuevo();
    const zoom = await page.evaluate(() => window.cadCentral.mapa.getZoom());

    // Mirando «SOS» llega un reporte: «Reportes» lleva el punto y suma uno; el mapa no va a un caso que no se ve.
    const cifraReportes = page.locator('[data-cifra-pestana="reportes"]');
    const antes = Number(await cifraReportes.textContent());
    const id = await enviarReporte(ciudadano, cerca(0.012, -0.01));
    await expect(pestana('Reportes')).toHaveClass(/bandeja__pestana--aviso/, { timeout: 10_000 });
    await expect(cifraReportes).toHaveText(String(antes + 1));
    expect(await page.evaluate(() => window.cadCentral.mapa.getZoom())).toBe(zoom);
    await pestana('Reportes').click();
    await expect(pestana('Reportes')).not.toHaveClass(/bandeja__pestana--aviso/);
    await expect(page.locator(`#revisar-${id}`)).toBeVisible();

    // Mirando «Reportes» sin nada elegido llega un SOS: la central pasa a «SOS» y lo enfoca.
    const sos = await enviarSos(ciudadano, cerca(-0.012, 0.01));
    await expect(pestana('SOS')).toHaveAttribute('aria-selected', 'true', { timeout: 10_000 });
    await expect(page.locator(`#sos-${sos}`)).toBeVisible();
    await expect(page.locator('.marcador--ultimo')).toHaveClass(/marcador-sos/);

    // Con un reporte elegido, otro SOS no le quita la pestaña: «SOS» lo avisa con el punto.
    await pestana('Reportes').click();
    await page.locator(`#revisar-${id} [data-seleccionar-revisar]`).click();
    await enviarSos(ciudadano, cerca(-0.014, 0.012));
    await expect(pestana('SOS')).toHaveClass(/bandeja__pestana--aviso/, { timeout: 10_000 });
    await expect(pestana('Reportes')).toHaveAttribute('aria-selected', 'true');
    // El lector de pantalla lo oye igual: el anuncio no está dentro de una pestaña oculta.
    const anuncio = page.locator('[data-anuncio]');
    await expect(anuncio).toHaveText(/^Nuevo SOS de Tania Ficticia/);
    expect(await anuncio.evaluate((el) => el.closest('[hidden]') === null)).toBe(true);
  });
});
