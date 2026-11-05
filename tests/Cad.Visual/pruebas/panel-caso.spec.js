// Panel del caso (05/11/2026), contra el servidor y la base reales: un reporte que llega desde la app se revisa, se decide y se
// asigna sin salir de la central. El panel se abre encima del mapa (no lo mueve), con pestañas y la acción fija al pie; al
// asignar se cierra solo, confirma lo hecho y el caso pasa a «En curso».
// Cambia el escenario (asigna un sereno): corre en el proyecto «llegadas», después de las capturas y de minimapa.spec.js.
import { test, expect } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { LIMA, leerSemilla, sesion } from '../soporte/entorno.js';
import { abrir, prepararPagina } from '../soporte/pagina.js';
import { api, token } from '../soporte/semilla.js';

const ahora = () => new Date().toISOString();

async function reporteNuevo() {
  const correo = `elsa.${randomUUID().slice(0, 8)}@correo.test`;
  const clave = `Clave-${randomUUID().slice(0, 8)}9`;
  await api('/api/cuentas/registro', { cuerpo: { nombres: 'Elsa', apellidos: 'Ficticia', correo, telefono: '900000007', contrasena: clave } });
  const ciudadana = await token(correo, clave);
  const categorias = await api('/api/reportes/categorias', { metodo: 'GET', token: ciudadana });
  return (await api('/api/reportes', {
    token: ciudadana,
    cuerpo: { idSolicitud: randomUUID(), categoriaId: categorias[0].id, descripcion: 'Robo de una mochila en el paradero (dato ficticio).',
      ocurridoAproximadamenteEn: ahora(), ubicacion: { fuente: 'Mapa', latitud: LIMA.latitud + 0.004, longitud: LIMA.longitud - 0.003, precisionMetros: null, referencia: null } },
  })).id;
}

test.describe('Central: panel del caso', () => {
  test.use({ storageState: sesion('operador'), reducedMotion: 'reduce' });

  test('Un reporte se revisa, se decide y se asigna sin salir de la central', async ({ page }, info) => {
    const { cuentas } = leerSemilla();
    // El suplente, operativo y cerca del hecho (en Lima, como el escenario): otras pruebas de este proyecto dejan a Sara con
    // un caso, así que el sugerido es el disponible más cercano, sea quien sea.
    const pedro = await token(cuentas.suplente.correo, cuentas.suplente.clave);
    await api('/api/sereno/estado', { metodo: 'PUT', token: pedro, cuerpo: { operativo: true, ubicacionAutorizada: true } });
    await api('/api/sereno/posiciones', { token: pedro, cuerpo: { latitud: LIMA.latitud + 0.0035, longitud: LIMA.longitud - 0.0025, precisionMetros: 6, obtenidaEn: ahora() } });
    const id = await reporteNuevo();

    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });
    const tamano = () => page.evaluate(() => JSON.stringify(window.cadCentral.mapa.getSize()));
    const antes = await tamano();

    // Revisar: se inicia la revisión y el reporte se abre en el panel, encima del mapa.
    await page.locator(`#revisar-${id}`).getByRole('button', { name: /^Revisar/ }).click();
    const panel = page.locator('[data-panel-caso]');
    await expect(panel).toBeVisible();
    const marco = page.frameLocator('[data-marco-caso]');
    await expect(marco.getByRole('tab', { name: 'Caso' })).toHaveAttribute('aria-selected', 'true');
    await expect(marco.getByRole('tab', { name: 'Ciudadano' })).toBeVisible();
    await expect(marco.getByRole('tab', { name: 'Historial y tiempos' })).toBeVisible();
    await expect(marco.locator('.panel-caso__pie')).toContainText('Decide qué hacer con el reporte');
    await expect(page.locator('[data-panel-titulo]')).toHaveText('Reporte · En revisión');
    expect(await tamano()).toBe(antes); // el mapa no cambia de tamaño
    await expect(page.locator(`#revisar-${id}`)).toContainText('En revisión'); // la bandeja ya lo sabe
    await page.screenshot({ path: info.outputPath('panel-revisar.png') });

    // La acción del paso se pliega a una línea para leer el caso con todo el alto, y se vuelve a abrir.
    const plegar = marco.locator('.panel-caso__plegar');
    await expect(plegar).toHaveAttribute('aria-expanded', 'true');
    const cuerpoAbierto = (await marco.locator('.panel-caso__cuerpo').boundingBox()).height;
    await plegar.click();
    await expect(plegar).toHaveAttribute('aria-expanded', 'false');
    await expect(marco.locator('#accion-del-paso')).toBeHidden();
    expect((await marco.locator('.panel-caso__cuerpo').boundingBox()).height).toBeGreaterThan(cuerpoAbierto + 150);
    await page.screenshot({ path: info.outputPath('panel-plegado.png') });
    await plegar.click();
    await expect(marco.locator('#accion-del-paso')).toBeVisible();

    // El aviso de «el caso cambió mientras escribías» sigue en la página del panel.
    await expect(marco.locator('[data-caso-cambio]')).toHaveCount(1);

    // La ficha del ciudadano, en su pestaña.
    await marco.getByRole('tab', { name: 'Ciudadano' }).click();
    await expect(marco.locator('#panel-ciudadano')).toContainText('Elsa Ficticia');

    // Esc con el foco dentro del panel lo cierra (nada a medias); «Decidir» lo vuelve a abrir.
    await marco.getByRole('tab', { name: 'Ciudadano' }).press('Escape');
    await expect(panel).toBeHidden();
    await page.locator(`#revisar-${id}`).getByRole('link', { name: /^Decidir/ }).click();
    await expect(panel).toBeVisible();
    await expect(marco.locator('.panel-caso__pie')).toContainText('Decide qué hacer con el reporte');

    // Decidir: admitir. El pie pasa a la asignación.
    const pie = marco.locator('.panel-caso__pie');
    await pie.locator('label').filter({ hasText: /^Admitir$/ }).first().click();
    await pie.locator('[name="Decision.Prioridad"]').selectOption({ index: 2 });
    await pie.locator('[name="Decision.Heridos"]').selectOption({ index: 2 });
    await pie.locator('[name="Decision.Motivo"]').fill('Enviar un sereno a verificar (dato ficticio).');
    // Con el formulario a medias, Esc no cierra el panel: lo escrito no se pierde por una tecla.
    await pie.locator('[name="Decision.Motivo"]').press('Escape');
    await expect(panel).toBeVisible();
    await pie.getByRole('button', { name: 'Registrar la decisión' }).click();
    await expect(pie).toContainText('Asigna el reporte a un sereno disponible');
    await expect(page.locator(`.central__reportes #sos-${id}`)).toBeVisible(); // pasó a «Por asignar»
    await page.screenshot({ path: info.outputPath('panel-asignar.png') });

    // Asignar al sugerido: el panel se cierra, lo confirma y el caso pasa a «En curso».
    await pie.locator('.asignar__sugerido').getByRole('button', { name: /^Asignar a / }).click();
    await expect(panel).toBeHidden({ timeout: 10_000 });
    await expect(page.locator('[data-aviso-flotante]')).toHaveText('Reporte asignado.');
    await expect(page.locator(`#asignado-${id}`)).toBeVisible();
    expect(await tamano()).toBe(antes);
  });
});
