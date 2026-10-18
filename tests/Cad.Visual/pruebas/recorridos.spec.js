// Regresión visual: cada pantalla, por rol, a 1366, 1920 y 390 px, contra su referencia aprobada.
import { test, expect } from '@playwright/test';
import { leerSemilla, sesion } from '../soporte/entorno.js';
import { abrir, capturaCompleta, guardarHtml, normalizarTextoDinamico, prepararPagina } from '../soporte/pagina.js';
import { pantallas } from './pantallas.js';

for (const rol of [null, 'admin', 'operador']) {
  test.describe(`Pantallas ${rol ?? 'públicas'}`, () => {
    if (rol) test.use({ storageState: sesion(rol) });

    for (const pantalla of pantallas.filter((p) => p.rol === rol)) {
      test(pantalla.nombre, async ({ page }) => {
        const { ids } = leerSemilla();
        await prepararPagina(page);
        await abrir(page, pantalla.ruta(ids));
        if (pantalla.tiempoReal) await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });
        await normalizarTextoDinamico(page);
        if (test.info().project.name === 'escritorio-1366') await guardarHtml(page, pantalla.nombre);
        await expect(page).toHaveScreenshot(`${pantalla.nombre}.png`, capturaCompleta);
      });
    }
  });
}
