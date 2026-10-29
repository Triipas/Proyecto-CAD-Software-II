// Reglas del sistema de diseño CAD v2 que se comprueban en el navegador, en cada pantalla.
// Estrictas (fallan): tipografía, desbordamiento horizontal, objetivos de 24 px (WCAG 2.5.8), movimiento reducido.
// Informativas (se adjuntan al informe y se anotan): controles por debajo de 44 px y colores fuera de la paleta.
import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { RAIZ_REPO, leerSemilla, sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';
import { pantallas } from './pantallas.js';
import { deudaDe } from './deudas.js';

// Paleta: todos los valores hexadecimales declarados en tokens.css, en rgb() normalizado.
const PALETA = (() => {
  const css = readFileSync(join(RAIZ_REPO, 'src', 'Cad.Server', 'wwwroot', 'css', 'tokens.css'), 'utf8');
  const colores = new Set(['rgb(255, 255, 255)', 'rgb(0, 0, 0)']);
  for (const [, hex] of css.matchAll(/#([0-9a-f]{6})\b/gi)) {
    const [r, g, b] = [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16));
    colores.add(`rgb(${r}, ${g}, ${b})`);
  }
  return [...colores];
})();

for (const rol of [null, 'admin', 'operador']) {
  test.describe(`Sistema de diseño ${rol ?? 'públicas'}`, () => {
    if (rol) test.use({ storageState: sesion(rol) });

    for (const pantalla of pantallas.filter((p) => p.rol === rol)) {
      test(pantalla.nombre, async ({ page }, info) => {
        const { ids } = leerSemilla();
        await prepararPagina(page);
        await abrir(page, pantalla.ruta(ids));

        const medicion = await page.evaluate((paleta) => {
          const visibles = [...document.body.querySelectorAll('*')].filter((el) => {
            if (el.closest('.leaflet-container')) return false;
            const caja = el.getBoundingClientRect();
            const estilo = getComputedStyle(el);
            return caja.width > 0 && caja.height > 0 && estilo.visibility !== 'hidden' && estilo.display !== 'none';
          });

          const interactivos = visibles.filter((el) =>
            el.matches('button, select, textarea, input:not([type="hidden"]):not([type="radio"]):not([type="checkbox"]), [role="button"]'));
          // En las tarjetas de SOS, reportes y serenos el botón es el nombre, pero su ::after cubre toda la tarjeta: ese es su área real.
          const medida = (el) => { const c = (el.matches('.sos__seleccionar, .sereno__seleccionar') ? el.closest('li') : el).getBoundingClientRect(); return { alto: Math.round(c.height), ancho: Math.round(c.width) }; };
          const describir = (el) => `${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}${el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/).join('.') : ''} «${(el.innerText || el.value || el.getAttribute('aria-label') || '').trim().slice(0, 40)}»`;

          const fueraDePaleta = new Map();
          for (const el of visibles) {
            const estilo = getComputedStyle(el);
            for (const propiedad of ['color', 'background-color', 'border-top-color']) {
              const valor = estilo.getPropertyValue(propiedad);
              if (!valor || valor === 'rgba(0, 0, 0, 0)' || valor.startsWith('rgba')) continue;
              if (propiedad === 'border-top-color' && estilo.borderTopStyle === 'none') continue;
              if (!paleta.includes(valor)) fueraDePaleta.set(valor, describir(el));
            }
          }

          return {
            fuente: getComputedStyle(document.body).fontFamily,
            publicSansCargada: [...document.fonts].some((f) => f.family.replace(/"/g, '') === 'Public Sans' && f.status === 'loaded'),
            desbordamiento: document.documentElement.scrollWidth - window.innerWidth,
            menoresDe24: interactivos.filter((el) => medida(el).alto < 24 || medida(el).ancho < 24).map(describir),
            menoresDe44: interactivos.filter((el) => medida(el).alto < 44).map((el) => `${describir(el)} (${medida(el).alto} px)`),
            animacionesActivas: document.getAnimations().filter((a) => a.playState === 'running').length,
            fueraDePaleta: [...fueraDePaleta].map(([color, ejemplo]) => `${color} en ${ejemplo}`),
          };
        }, PALETA);

        await info.attach('sistema.json', { body: JSON.stringify(medicion, null, 2), contentType: 'application/json' });
        if (medicion.menoresDe44.length) info.annotations.push({ type: 'controles < 44 px', description: medicion.menoresDe44.join(' · ') });
        if (medicion.fueraDePaleta.length) info.annotations.push({ type: 'colores fuera de la paleta', description: medicion.fueraDePaleta.join(' · ') });

        expect.soft(medicion.fuente, 'La tipografía base es Public Sans').toMatch(/^"?Public Sans"?/);
        expect.soft(medicion.publicSansCargada, 'Public Sans se descargó del proyecto').toBe(true);
        expect.soft(medicion.desbordamiento, 'Sin desplazamiento horizontal').toBeLessThanOrEqual(0);
        const deuda = deudaDe(pantalla.nombre);
        const esConocido = (descripcion) => deuda.objetivos.some((fragmento) => descripcion.includes(fragmento));
        if (medicion.menoresDe24.some(esConocido)) info.annotations.push({ type: 'deuda conocida (objetivos)', description: deuda.motivo });
        expect.soft(medicion.menoresDe24.filter((d) => !esConocido(d)), 'Objetivos de al menos 24 × 24 px (WCAG 2.5.8)').toEqual([]);
        expect.soft(medicion.animacionesActivas, 'Sin animaciones con movimiento reducido').toBe(0);
      });
    }
  });
}
