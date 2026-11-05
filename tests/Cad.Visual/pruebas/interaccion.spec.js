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
    // Sin distinguir mayúsculas ni tildes: «SARÁ» encuentra a «Sara».
    await buscar.fill('SARÁ');
    await expect(barra.getByRole('option')).toHaveCount(1);
    await buscar.fill('zzzz');
    await expect(barra.getByText('Ningún sereno disponible se llama así.')).toBeVisible();
  });

  test('la tarjeta de un sereno con ubicación es un botón que lo elige; sin ubicación no hay botón', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    // Los serenos se abren encima del mapa desde su cifra (05/11/2026).
    await page.locator('.mapa-herramientas [data-abrir-serenos]').click();
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

  // Bandeja (05/11/2026): el trabajo en el orden en que se hace, y los serenos encima del mapa cuando se piden.
  test('la bandeja ordena el trabajo y los serenos se abren encima del mapa sin moverlo', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    // Los grupos, en el orden del trabajo; los reportes por revisar ya están en la central, con su acción.
    const titulos = await page.locator('.bandeja__grupo .central__titulo').allTextContents();
    expect(titulos.map((t) => t.replace(/\s*\d+\s*$/, '').trim())).toEqual(
      ['Atención urgente · SOS', 'Por revisar · reportes', 'Por asignar · reportes', 'En curso · con sereno']);
    const revisar = page.locator('[data-region-revisar] [data-revisar]');
    await expect(revisar).toHaveCount(2);
    await expect(page.locator('[data-region-revisar] [data-estado="Recibido"]').getByRole('button', { name: /^Revisar/ })).toBeVisible();
    await expect(page.locator('[data-region-revisar] [data-estado="En revisión"]').getByRole('link', { name: /^Decidir/ })).toBeVisible();
    await expect(page.locator('[data-contador-revisar]')).toHaveText('2');

    // Tocar la tarjeta de un reporte por revisar (no su botón) lo elige: con punto, su marcador queda elegido en el mapa.
    const enRevision = page.locator('[data-region-revisar] [data-estado="En revisión"]');
    const caja = await enRevision.boundingBox();
    await page.mouse.click(caja.x + caja.width * 0.75, caja.y + 20);
    await expect(enRevision).toHaveClass(/sos--seleccionado/);
    await expect(enRevision.locator('[data-seleccionar-revisar]')).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#mapa .marcador-caso--revision.marcador--elegido')).toHaveCount(1);
    await page.keyboard.press('Escape');

    // El mapa no se toca al abrir los serenos: mismo centro y mismo tamaño; el panel lo tapa.
    const vista = () => page.evaluate(() => JSON.stringify([window.cadCentral.mapa.getCenter(), window.cadCentral.mapa.getSize()]));
    const antes = await vista();
    const cifra = page.locator('.barra [data-abrir-serenos]');
    await expect(page.locator('#panel-serenos')).toBeHidden();
    await cifra.click();
    await expect(page.locator('#panel-serenos')).toBeVisible();
    await expect(cifra).toHaveAttribute('aria-expanded', 'true');
    expect(await vista()).toBe(antes);
    await expect(page.locator('[data-grupo-serenos="disponibles"]')).toContainText('Sara Serena Ficticia');
    await page.keyboard.press('Escape');
    await expect(page.locator('#panel-serenos')).toBeHidden();
    await expect(page.locator('.mapa-herramientas [data-abrir-serenos]')).toBeFocused();

    // El reporte admitido conserva el enlace a su detalle (sin punto en el mapa no hay globo desde donde abrirlo).
    await expect(page.locator('.central__reportes').getByRole('link', { name: /^Ver el reporte/ })).toHaveAttribute('href', /\/panel\/reportes\//);
    // «Asignar» en un reporte admitido abre la barra de asignación con el sereno sugerido.
    await page.locator('.central__reportes').getByRole('button', { name: /^Asignar/ }).click();
    await expect(page.locator('[data-asignar-rapido]')).toBeVisible();
    await expect(page.locator('[data-ar-sugerido]')).toContainText('Sara Serena Ficticia');
  });

  test('en el teléfono, el panel de serenos se abre sobre el mapa y se cierra con su botón', async ({ page }, info) => {
    test.skip(info.project.name !== 'movil-390', 'solo en pantallas estrechas');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await page.locator('.mapa-herramientas [data-abrir-serenos]').click();
    const cerrar = page.getByRole('button', { name: 'Cerrar la lista de serenos' });
    await expect(cerrar).toBeVisible();
    await expect(cerrar).toBeFocused();
    await cerrar.click();
    await expect(page.locator('#panel-serenos')).toBeHidden();
  });

  test('los casos con sereno siguen en la cola y su recorrido solo se dibuja al elegirlos', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    // La semilla compartida no se toca: un SOS y un reporte «ya asignados» se simulan con fragmentos de la central.
    const sos = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const reporte = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    const serenoSos = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    const serenoReporte = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
    const punto = { [sos]: [-12.043, -77.035], [reporte]: [-12.051, -77.0325] };
    const tarjeta = (id, origen, sereno, nombre, ciudadano) => `<li class="sos sos--asignado${origen === 'Reporte' ? ' sos--reporte' : ''}" id="asignado-${id}" tabindex="-1" data-asignado="${id}" data-origen="${origen}" data-sereno="${sereno}" data-clave="${id}" data-lat="${punto[id][0]}" data-lon="${punto[id][1]}"><div class="sos__cabecera"><span class="etiqueta-${origen === 'Reporte' ? 'reporte' : 'urgente'}">${origen}</span></div><button type="button" class="sos__ciudadano sos__seleccionar" data-seleccionar-asignado aria-pressed="false">${ciudadano}</button><p class="sos__dato-ubicacion"><span>Sereno: ${nombre}</span></p></li>`;
    const casoMapa = (id, origen, ciudadano) => `<li data-caso-mapa="${id}" data-origen="${origen}" data-estado="Asignada" data-detalle="/panel/central/sos/${id}" data-ciudadano="${ciudadano}" data-lat="${punto[id][0]}" data-lon="${punto[id][1]}" data-clave="${id}"></li>`;
    const sereno = (id, nombre, caso, lat, lon) => `<li class="sereno" id="sereno-${id}" data-sereno="${id}" data-nombre="${nombre}" data-disponible="false" data-lat="${lat}" data-lon="${lon}" data-caso="${caso}" data-hora="10:00:00" data-clave="${id}"><div class="sereno__cabecera"><span class="sereno__nombre">${nombre}</span></div></li>`;
    await page.route(/handler=Asignados/, (ruta) => ruta.fulfill({ contentType: 'text/html', body: `<div data-lista-asignados data-total="2"><ol class="lista-sos">${tarjeta(sos, 'SOS', serenoSos, 'Sereno Uno', 'Persona Uno')}${tarjeta(reporte, 'Reporte', serenoReporte, 'Sereno Dos', 'Persona Dos')}</ol></div>` }));
    await page.route(/handler=CasosMapa/, (ruta) => ruta.fulfill({ contentType: 'text/html', body: `<ul class="visualmente-oculto" data-casos-mapa data-total="2">${casoMapa(sos, 'SOS', 'Persona Uno')}${casoMapa(reporte, 'Reporte', 'Persona Dos')}</ul>` }));
    await page.route(/handler=Serenos/, (ruta) => ruta.fulfill({ contentType: 'text/html', body: `<div data-lista-serenos data-operativos="2" data-disponibles="0"><ul class="lista-serenos">${sereno(serenoSos, 'Sereno Uno', sos, -12.0455, -77.037)}${sereno(serenoReporte, 'Sereno Dos', reporte, -12.0535, -77.03)}</ul></div>` }));
    await page.evaluate(async () => { await window.cadCentral.refrescar(); await window.cadCentral.refrescarSerenos(); });

    // Siguen en la cola, junto a los pendientes, y en el mapa no hay ninguna ruta mientras no se elija un caso.
    const asignados = page.locator('[data-asignado]');
    await expect(asignados).toHaveCount(2);
    await expect(page.locator('[data-contador-asignados]')).toHaveText('2');
    await expect(page.locator('path.linea-vinculo')).toHaveCount(0);

    // Elegir el SOS asignado: su recorrido (y solo el suyo), con la tarjeta marcada.
    const botonSos = page.locator(`#asignado-${sos} [data-seleccionar-asignado]`);
    await botonSos.click();
    await expect(botonSos).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('path.linea-vinculo--sos').first()).toBeAttached();
    await expect(page.locator('path.linea-vinculo--reporte')).toHaveCount(0);

    // Elegir el reporte: la ruta del SOS desaparece y aparece la del reporte.
    await page.locator(`#asignado-${reporte} [data-seleccionar-asignado]`).click();
    await expect(botonSos).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('path.linea-vinculo--reporte').first()).toBeAttached();
    await expect(page.locator('path.linea-vinculo--sos')).toHaveCount(0);

    // Elegir un caso pendiente suelta al asignado: ninguna ruta.
    await botonSos.click();
    await expect(page.locator('path.linea-vinculo--sos').first()).toBeAttached();
    await page.locator('.central__urgente [data-seleccionar-sos]').first().click();
    await expect(botonSos).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('path.linea-vinculo')).toHaveCount(0);
  });

  test('las capas del mapa se ocultan desde «Capas y leyenda»', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');

    await page.getByRole('button', { name: 'Capas y leyenda' }).click();
    const capaSos = page.locator('[data-capa="sos"]');
    await capaSos.click();
    await expect(capaSos).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('#mapa .marcador-sos').first()).toBeHidden();
  });

  // 05/11/2026: la leyenda lleva el icono de cada capa y una clave con cada marcador y cada línea del mapa.
  test('la clave del mapa explica cada icono y se cierra con Esc', async ({ page }, info) => {
    test.skip(info.project.name === 'escritorio-1920', 'basta con escritorio y móvil');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

        const boton = page.getByRole('button', { name: 'Capas y leyenda' });
    await expect(boton).toHaveAttribute('aria-expanded', 'false');
    await boton.click();
    const clave = page.getByRole('region', { name: 'Capas y leyenda' });
    await expect(clave).toBeVisible();
    await expect(boton).toHaveAttribute('aria-expanded', 'true');
    // Cada capa lleva su icono real (el de marcadores.js), no un cuadrado de color.
    await expect(clave.locator('[data-capa] .leyenda-icono svg')).toHaveCount(3);
    for (const texto of ['SOS por asignar', 'SOS en curso', 'Reporte admitido', 'Reporte por revisar', 'Reporte en curso',
      'Disponible', 'No disponible', 'En el lugar', 'Sereno sugerido', 'Recorrido', 'Anillos'])
      await expect(clave.getByText(texto, { exact: true })).toBeVisible();
    // Las muestras son los mismos marcadores: el reporte por revisar lleva el borde discontinuo.
    expect(await clave.locator('.marcador-caso--revision').evaluate((el) => getComputedStyle(el).borderTopStyle)).toBe('dashed');
    await expect(clave.locator('.clave-mapa__grupos .leyenda-icono svg')).toHaveCount(9);
    const caja = await clave.boundingBox();
    expect(caja.y).toBeGreaterThanOrEqual(0); // no se sale de la pantalla por arriba

    // Ocultar una capa esconde sus marcadores del mapa, no sus muestras de la clave.
    await clave.locator('[data-capa="sos"]').click();
    await expect(clave.locator('.marcador-sos').first()).toBeVisible();
    await clave.locator('[data-capa="sos"]').click();

    await clave.getByRole('button', { name: 'Cerrar las capas y la leyenda' }).focus();
    await page.keyboard.press('Escape');
    await expect(clave).toBeHidden();
    await expect(boton).toBeFocused();
    await expect(boton).toHaveAttribute('aria-expanded', 'false');
    // A 390 px la página no se desplaza de lado (antes «Salir» se salía de la barra).
    if (info.project.name === 'movil-390')
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  });
});
