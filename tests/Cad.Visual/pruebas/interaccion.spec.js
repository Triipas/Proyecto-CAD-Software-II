// Interacción de la central «Mapa protagonista» (fase 3): línea de despacho, diálogo de asignación y paneles.
// No confirma la asignación: el escenario sembrado debe seguir igual para las capturas.
import { test, expect } from '@playwright/test';
import { sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';

test.describe('Central: interacción', () => {
  test.use({ storageState: sesion('operador') });

  test('elegir un SOS muestra la línea de despacho y el diálogo de asignación es accesible', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    // Elegir el SOS pendiente: aparece la distancia al sereno disponible más cercano.
    await page.locator('.central__urgente [data-seleccionar-sos]').first().click();
    await expect(page.locator('.etiqueta-despacho')).toHaveText(/\d+ (m|km)/);
    await expect(page.locator('.sereno--sugerido [data-distancia]')).toContainText('Más cercano al caso elegido');

    // Abrir la confirmación desde la barra de asignación rápida: es un diálogo modal con el foco dentro.
    const asignar = page.locator('[data-ar-sugerido]');
    await asignar.click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo).toBeVisible();
    await expect(dialogo.getByRole('heading')).toContainText('¿Asignar el SOS de Ana Ficticia');
    await expect(dialogo.getByRole('button', { name: 'Confirmar asignación' })).toBeFocused();

    // El foco no sale del diálogo y Esc lo cierra devolviendo el foco al botón que lo abrió.
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    await expect(dialogo.getByRole('button', { name: 'Confirmar asignación' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(dialogo).toBeHidden();
    await expect(asignar).toBeFocused();
  });

  test('la barra de asignación rápida ofrece al más cercano y busca a otro por nombre', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    const barra = page.locator('[data-asignar-rapido]');
    await expect(barra).toBeHidden();
    await page.locator('.central__urgente [data-seleccionar-sos]').first().click();
    await expect(barra).toBeVisible();

    // El más cercano, a un clic: abre la misma confirmación que el panel (sin confirmarla).
    const sugerido = barra.locator('[data-ar-sugerido]');
    await expect(sugerido).toContainText(/\d+ (m|km)/);
    await sugerido.click();
    const dialogo = page.getByRole('dialog');
    await expect(dialogo.getByRole('heading')).toContainText('¿Asignar el SOS de Ana Ficticia');
    await page.keyboard.press('Escape');
    await expect(dialogo).toBeHidden();

    // «/» enfoca el buscador; un nombre que no existe no deja opciones.
    await page.keyboard.press('/');
    const buscar = barra.locator('[data-ar-buscar]');
    await expect(buscar).toBeFocused();
    await expect(barra.getByRole('option').first()).toBeVisible();
    await buscar.fill('zzzz');
    await expect(barra.getByText('Ningún sereno disponible se llama así.')).toBeVisible();
  });

  test('la tarjeta de un sereno con ubicación es un botón que lo elige; sin ubicación no hay botón', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    const tarjeta = page.locator('.sereno:has([data-ver-sereno])').first();
    const boton = tarjeta.locator('[data-ver-sereno]');
    await expect(boton).toHaveAttribute('aria-pressed', 'false');
    // Se pulsa en el cuerpo de la tarjeta, no en el nombre: el área del botón cubre toda la tarjeta.
    const caja = await tarjeta.boundingBox();
    await page.mouse.click(caja.x + caja.width - 8, caja.y + caja.height - 8);
    await expect(boton).toHaveAttribute('aria-pressed', 'true');
    await expect(tarjeta).toHaveClass(/sereno--seleccionado/);

    // La selección sobrevive a la resincronización de la lista.
    await page.evaluate(() => window.cadCentral.refrescarSerenos());
    await expect(boton).toHaveAttribute('aria-pressed', 'true');
    await expect(tarjeta).toHaveClass(/sereno--seleccionado/);

    // Sin ubicación (Pedro Suplente): la tarjeta no ofrece botón.
    await expect(page.locator('.sereno', { hasText: 'Ubicación no disponible' }).locator('[data-ver-sereno]')).toHaveCount(0);
  });

  test('los paneles se pliegan y las capas del mapa se ocultan desde la leyenda', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');

    const plegar = page.locator('#panel-cola [data-plegar]');
    await plegar.click();
    await expect(plegar).toHaveAttribute('aria-expanded', 'false');
    await expect(page.locator('#panel-cola')).toHaveClass(/panel-flotante--plegado/);
    await plegar.click();
    await expect(plegar).toHaveAttribute('aria-expanded', 'true');

    const capaSos = page.locator('[data-capa="sos"]');
    await capaSos.click();
    await expect(capaSos).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('.marcador-sos').first()).toBeHidden();
  });
});
