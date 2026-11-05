// Minimapa del detalle de un SOS con los serenos (05/11/2026), contra el servidor y la base reales:
// - se ven los serenos operativos con ubicación y una línea recta, con su distancia, hasta el sereno elegido para asignar
//   (el sugerido; al elegir a otro de la lista, la línea pasa a él);
// - con el caso asignado, el recorrido por calles del sereno hasta el caso (red de calles de Arequipa).
// Cambia el escenario (posiciones y una asignación): corre en el proyecto «llegadas», después de las capturas y de llegada.spec.js.
import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { LIMA, leerSemilla, sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';
import { api, token } from '../soporte/semilla.js';

const ahora = () => new Date().toISOString();
const cerca = (dLat, dLon) => ({ latitud: LIMA.latitud + dLat, longitud: LIMA.longitud + dLon });

async function ciudadanoNuevo() {
  const correo = `dina.${randomUUID().slice(0, 8)}@correo.test`;
  const clave = `Clave-${randomUUID().slice(0, 8)}9`;
  await api('/api/cuentas/registro', { cuerpo: { nombres: 'Dina', apellidos: 'Ficticia', correo, telefono: '900000008', contrasena: clave } });
  return token(correo, clave);
}

const sosEn = async (punto) => (await api('/api/sos', {
  token: await ciudadanoNuevo(),
  cuerpo: { idSolicitud: randomUUID(), activadoEn: ahora(), ubicacion: { ...punto, precisionMetros: 5, obtenidaEn: ahora() } },
})).id;

async function serenoEn(cuenta, punto) {
  const t = await token(cuenta.correo, cuenta.clave);
  await api('/api/sereno/estado', { metodo: 'PUT', token: t, cuerpo: { operativo: true, ubicacionAutorizada: true } });
  await api('/api/sereno/posiciones', { token: t, cuerpo: { ...punto, precisionMetros: 6, obtenidaEn: ahora() } });
}

test.describe('Detalle del SOS: serenos en el minimapa', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'reduce' });

  test('Muestra los serenos y la distancia en línea recta al sereno elegido para asignar', async ({ page }, info) => {
    const { cuentas } = leerSemilla();
    await serenoEn(cuentas.serena, cerca(0.0035, -0.0010));
    await serenoEn(cuentas.suplente, cerca(-0.0040, 0.0050));
    const id = await sosEn(cerca(0.0020, -0.0005));

    await prepararPagina(page);
    await abrir(page, `/panel/central/sos/${id}`);
    const minimapa = page.locator('#mapa-hecho');
    await expect(minimapa.locator('.marcador-sereno')).toHaveCount(2);
    await expect(minimapa.locator('path.linea-despacho')).toHaveCount(1);
    // La línea va al sugerido (el más cercano) y lleva la misma distancia que el bloque de asignación.
    const asignar = page.locator('[data-asignar-caso]').first();
    const distanciaSugerido = (await asignar.locator('.asignar__distancia').textContent()).trim();
    await expect(minimapa.locator('.etiqueta-despacho')).toHaveText(distanciaSugerido);
    await expect(minimapa.locator('.marcador-sereno.marcador--elegido')).toHaveAttribute('title', /^Sara Serena/);
    await page.screenshot({ path: info.outputPath('minimapa-sugerido.png') });

    // Al elegir a otro sereno de la lista, la línea y su distancia pasan a él.
    await page.locator('details.otras-acciones > summary').click();
    await asignar.locator('details.asignar__otros > summary').click();
    const pedro = asignar.locator('[data-asignar-opcion][data-nombre^="Pedro"]');
    const textoPedro = await pedro.textContent();
    await pedro.locator('input').check();
    await expect(minimapa.locator('.marcador-sereno.marcador--elegido')).toHaveAttribute('title', /^Pedro Suplente/);
    await expect(minimapa.locator('.etiqueta-despacho')).toHaveCount(1); // la anterior se desvanece en 200 ms
    const etiqueta = (await minimapa.locator('.etiqueta-despacho').textContent()).trim();
    expect(etiqueta).not.toBe(distanciaSugerido);
    expect(textoPedro).toContain(etiqueta);
    await expect(minimapa.locator('path.linea-despacho')).toHaveCount(1);
    await minimapa.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath('minimapa-otro.png') });
  });

  test('Con el caso asignado, muestra el recorrido por calles del sereno hasta el caso', async ({ page }, info) => {
    const { cuentas } = leerSemilla();
    // En Arequipa, donde está la red de calles: el sereno a ~400 m del SOS.
    await serenoEn(cuentas.serena, { latitud: -16.4024, longitud: -71.5369 });
    const id = await sosEn({ latitud: -16.3988, longitud: -71.5369 });

    await prepararPagina(page);
    await abrir(page, `/panel/central/sos/${id}`);
    await page.locator('details.otras-acciones > summary').click();
    await page.getByRole('button', { name: /^Asignar a Sara Serena/ }).first().click();
    await page.waitForLoadState('networkidle');

    const minimapa = page.locator('#mapa-hecho');
    await expect(minimapa.locator('path.linea-vinculo')).toHaveCount(2); // borde blanco y trazo
    expect(await minimapa.locator('path.linea-vinculo').last().getAttribute('stroke-dasharray')).toBeNull(); // no es la punteada
    await expect(minimapa.locator('.marcador-sereno[title^="Sara Serena"]')).toHaveAttribute('title', /asignado a este caso · a 400 m/);
    await minimapa.scrollIntoViewIfNeeded();
    await page.screenshot({ path: info.outputPath('minimapa-recorrido.png') });
  });
});
