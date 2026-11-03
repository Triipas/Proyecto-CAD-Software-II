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

  test('el filtro de la cola también filtra el mapa: «SOS» solo SOS, «Reportes» solo reportes', async ({ page }, info) => {
    test.skip(info.project.name !== 'escritorio-1366', 'basta con un ancho');
    await prepararPagina(page);
    await abrir(page, '/panel/central');
    await expect(page.getByText('Conectado en tiempo real')).toBeVisible({ timeout: 15_000 });

    const capa = (nombre) => page.locator(`[data-capa="${nombre}"]`);
    const filtro = (nombre) => page.locator(`[data-filtro-cola="${nombre}"]`);
    // Qué marcadores se ven (por estilo: la semilla no los deja dentro del encuadre).
    const vistos = (selector) => page.evaluate((s) => [...document.querySelectorAll(s)].map((e) => getComputedStyle(e).display !== 'none'), selector);

    // Con todo, se ve todo.
    expect(await vistos('.marcador-sos')).toContain(true);
    expect(await vistos('.marcador-reporte')).toContain(true);

    // «SOS»: ningún reporte en el mapa (ni el pendiente ni el que está en curso), y su chip lo dice.
    await filtro('sos').click();
    await expect(capa('reporte')).toHaveAttribute('aria-pressed', 'false');
    await expect(capa('sos')).toHaveAttribute('aria-pressed', 'true');
    const reportes = await vistos('.marcador-reporte');
    expect(reportes.length).toBeGreaterThan(0);
    expect(reportes).not.toContain(true);
    expect(await vistos('.marcador-sos')).toContain(true);

    // «Reportes»: ningún SOS.
    await filtro('reportes').click();
    await expect(capa('sos')).toHaveAttribute('aria-pressed', 'false');
    await expect(capa('reporte')).toHaveAttribute('aria-pressed', 'true');
    expect(await vistos('.marcador-sos')).not.toContain(true);
    expect(await vistos('.marcador-reporte')).toContain(true);

    // «Todo» los devuelve.
    await filtro('todo').click();
    await expect(capa('sos')).toHaveAttribute('aria-pressed', 'true');
    await expect(capa('reporte')).toHaveAttribute('aria-pressed', 'true');
    expect(await vistos('.marcador-reporte')).toContain(true);

    // El caso elegido que queda fuera del filtro se suelta: la barra de asignación se cierra.
    const barra = page.locator('[data-asignar-rapido]');
    await page.locator('.central__reportes [data-seleccionar-sos]').first().click();
    await expect(barra).toBeVisible();
    await filtro('sos').click();
    await expect(barra).toBeHidden();
    await expect(page.locator('.sos--seleccionado')).toHaveCount(0);

    // Uno de su propio grupo sí se conserva.
    await page.locator('.central__urgente [data-seleccionar-sos]').first().click();
    await expect(barra).toBeVisible();
    await filtro('sos').click();
    await expect(barra).toBeVisible();
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

    // El filtro de la cola los separa: «SOS» deja el SOS asignado y suelta el recorrido del reporte.
    await page.locator('[data-filtro-cola="sos"]').click();
    await expect(page.locator('[data-asignado]:visible')).toHaveCount(1);
    await expect(page.locator('[data-contador-asignados]')).toHaveText('1');
    await expect(page.locator('path.linea-vinculo')).toHaveCount(0);
    await page.locator('[data-filtro-cola="todo"]').click();

    // Elegir un caso pendiente suelta al asignado: ninguna ruta.
    await botonSos.click();
    await expect(page.locator('path.linea-vinculo--sos').first()).toBeAttached();
    await page.locator('.central__urgente [data-seleccionar-sos]').first().click();
    await expect(botonSos).toHaveAttribute('aria-pressed', 'false');
    await expect(page.locator('path.linea-vinculo')).toHaveCount(0);
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
