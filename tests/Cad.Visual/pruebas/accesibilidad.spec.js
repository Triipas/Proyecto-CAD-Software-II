// Accesibilidad con axe-core: WCAG 2.0, 2.1 y 2.2 niveles A y AA, en cada pantalla, a 1366 y 390 px.
import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { leerSemilla, sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';
import { pantallas } from './pantallas.js';
import { deudaDe } from './deudas.js';

const ETIQUETAS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

for (const rol of [null, 'admin', 'operador']) {
  test.describe(`Accesibilidad ${rol ?? 'públicas'}`, () => {
    if (rol) test.use({ storageState: sesion(rol) });

    for (const pantalla of pantallas.filter((p) => p.rol === rol)) {
      test(pantalla.nombre, async ({ page }, info) => {
        test.skip(info.project.name === 'escritorio-1920', 'axe se ejecuta a 1366 y 390 px');
        const { ids } = leerSemilla();
        await prepararPagina(page);
        await abrir(page, pantalla.ruta(ids));

        const resultado = await new AxeBuilder({ page })
          .withTags(ETIQUETAS)
          // Los controles y atribuciones de Leaflet son de la librería, no del panel.
          .exclude('.leaflet-control-container')
          .analyze();

        const resumen = resultado.violations.map((v) => ({
          regla: v.id,
          impacto: v.impact,
          descripcion: v.help,
          elementos: v.nodes.map((n) => n.target.join(' ')).slice(0, 5),
        }));
        await info.attach('axe.json', { body: JSON.stringify(resumen, null, 2), contentType: 'application/json' });
        const deuda = deudaDe(pantalla.nombre);
        const conocidas = resumen.filter((v) => deuda.axe.includes(v.regla));
        if (conocidas.length) info.annotations.push({ type: 'deuda conocida (axe)', description: `${conocidas.map((v) => v.regla).join(', ')}: ${deuda.motivo}` });
        expect(resumen.filter((v) => !deuda.axe.includes(v.regla)), `Infracciones de accesibilidad nuevas en ${pantalla.nombre}`).toEqual([]);
      });
    }
  });
}
