// El rumbo sale de posiciones simuladas en el HTML de respuesta: estas pruebas no escriben posiciones en la base.
import { test, expect } from '@playwright/test';
import { sesion } from '../soporte/entorno.js';
import { prepararPagina } from '../soporte/pagina.js';

test.use({ storageState: process.env.CAD_SPRITES_SESION ?? sesion('operador') });

async function preparar(page) {
  await prepararPagina(page);
  await page.goto('/panel/central');
  await page.waitForFunction(() => window.cadCentral);
  const respuesta = await page.request.get('/panel/central?handler=Serenos');
  let html = await respuesta.text();
  const datos = await page.locator('[data-region-serenos] [data-sereno][data-lat]:not([data-lat=""])').first().evaluate((el) => ({ ...el.dataset }));
  let instante = Date.parse(datos.posicionEn) || Date.now();
  await page.route('**/panel/central?handler=Serenos', (ruta) => ruta.fulfill({ contentType: 'text/html', body: html }));
  const modificar = async (cambios) => {
    if (('lat' in cambios || 'lon' in cambios) && !('posicionEn' in cambios))
      cambios = { ...cambios, posicionEn: new Date(instante += 15000).toISOString() };
    html = await page.evaluate(({ html, id, cambios }) => {
      const doc = new DOMParser().parseFromString(html, 'text/html');
      Object.assign(doc.querySelector(`[data-sereno="${id}"]`).dataset, cambios);
      return doc.body.innerHTML;
    }, { html, id: datos.sereno, cambios });
    await page.evaluate(() => cadCentral.refrescarSerenos());
  };
  // El título de Leaflet permite localizar al mismo sereno aunque cambien su disponibilidad o su forma.
  const vehiculo = page.locator(`#mapa .leaflet-marker-icon[title*="${datos.nombre}"]`);
  return { datos, modificar, vehiculo };
}

test('la camioneta sigue las ocho direcciones y conserva su nodo, selección, foco y globo', async ({ page }) => {
  const { datos, modificar, vehiculo } = await preparar(page);
  await vehiculo.evaluate((el) => { el.pruebaCamioneta = true; el.click(); el.focus(); });
  let lat = Number(datos.lat), lon = Number(datos.lon);
  for (const [indice, nombre] of ['N', 'NE', 'E', 'SE', 'S', 'SO', 'O', 'NO'].entries()) {
    const angulo = indice * Math.PI / 4;
    lat += Math.cos(angulo) * 20 / 111195;
    lon += Math.sin(angulo) * 20 / (111195 * Math.cos(lat * Math.PI / 180));
    await modificar({ lat: String(lat), lon: String(lon) });
    await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', nombre);
    expect(await vehiculo.evaluate((el) => el.pruebaCamioneta)).toBe(true);
  }
  await modificar({ lat: String(lat), lon: String(lon) });
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'NO');
  // Ruido de ~1 m y un cambio de estado no reinician el rumbo ni recrean el marcador.
  await modificar({ lat: String(lat + 0.000009), disponible: 'false' });
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'NO');
  await expect(vehiculo.locator('[data-camioneta]')).toHaveClass(/camioneta--no-disponible/);
  const gris = await vehiculo.locator('.camioneta__sprite').evaluate((el) => ({ imagen: getComputedStyle(el).backgroundImage, filtro: getComputedStyle(el).filter }));
  expect(gris.imagen).toContain('camioneta-gris-direcciones.png');
  expect(gris.filtro).toBe('none');
  await expect(vehiculo).toHaveAttribute('title', /no disponible/);
  expect(await vehiculo.evaluate((el) => el.pruebaCamioneta)).toBe(true);
  await expect(vehiculo).toBeFocused();
  await expect(vehiculo).toHaveClass(/marcador--elegido/);
  await expect(page.locator('#mapa .leaflet-popup')).toHaveCount(1);
  await expect(page.locator('#panel-serenos .sereno__escudo svg').first()).toBeAttached();
  await expect(page.locator('#panel-serenos [data-camioneta]')).toHaveCount(0);
  await modificar({ disponible: 'true' });
  expect(await vehiculo.locator('.camioneta__sprite').evaluate((el) => getComputedStyle(el).backgroundImage)).toContain('camioneta-verde-direcciones.png');
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'NO');
});

test('filtra el ruido y evita alternar vistas cerca del límite entre sectores', async ({ page }) => {
  await page.goto('/panel/central');
  const resultado = await page.evaluate(() => {
    const origen = [-16.43, -71.53];
    const mover = (rumbo, metros) => [origen[0] + Math.cos(rumbo * Math.PI / 180) * metros / 111195,
      origen[1] + Math.sin(rumbo * Math.PI / 180) * metros / (111195 * Math.cos(origen[0] * Math.PI / 180))];
    const inicial = cadMarcadores.orientarSereno(null, origen);
    const este = { ...inicial, rumbo: 90, direccion: 2, confirmada: true };
    return {
      ruido: cadMarcadores.orientarSereno(este, mover(0, 2)) === este,
      dentro: cadMarcadores.orientarSereno(este, mover(65, 20)).direccion,
      fuera: cadMarcadores.orientarSereno(este, mover(60, 20)).direccion,
      lento: cadMarcadores.orientarSereno(cadMarcadores.orientarSereno(este, mover(0, 2)), mover(0, 5)).direccion,
      invalido: cadMarcadores.orientarSereno(este, [NaN, -71]) === este,
    };
  });
  expect(resultado).toEqual({ ruido: true, dentro: 2, fuera: 1, lento: 0, invalido: true });
});

test('sin caso ni ruta se orienta con el GPS, conserva el rumbo al parar y lo cambia al reanudar', async ({ page }) => {
  let rutasPedidas = 0;
  page.on('request', (solicitud) => { if (solicitud.url().includes('handler=Rutas')) rutasPedidas++; });
  const { datos, modificar, vehiculo } = await preparar(page);
  const lat = Number(datos.lat), lon = Number(datos.lon);
  await modificar({ caso: '', disponible: 'true', precision: '5', lat: String(lat), lon: String(lon) });
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-rumbo', 'sin-confirmar');
  await vehiculo.evaluate((el) => el.click());
  await expect(page.locator('#mapa .leaflet-popup')).toContainText('Sin rumbo confirmado');
  await modificar({ lon: String(lon + 0.0003) });
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'E');
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-rumbo', 'observado');
  await expect(page.locator('#mapa .leaflet-popup')).toContainText('Este · último desplazamiento');
  await modificar({ lat: String(lat + 0.00001) }); // deriva de ~1 m estando detenido
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'E');
  await modificar({ lat: String(lat + 0.0003) }); // reanuda hacia el norte
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'N');
  expect(rutasPedidas).toBe(0);
});

test('ignora lecturas repetidas, incertidumbre GPS y saltos tras perder conexión', async ({ page }) => {
  await page.goto('/panel/central');
  const resultado = await page.evaluate(() => {
    const origen = [-16.43, -71.53];
    const inicio = Date.parse('2026-11-09T15:00:00Z');
    const lectura = (segundos, precisionMetros = 5) => ({ recibidaEn: new Date(inicio + segundos * 1000).toISOString(), precisionMetros });
    const inicial = cadMarcadores.orientarSereno(null, origen, lectura(0));
    const este = cadMarcadores.orientarSereno(inicial, [-16.43, -71.5297], lectura(15));
    const repetida = cadMarcadores.orientarSereno(este, [-16.429, -71.53], lectura(15));
    const atrasada = cadMarcadores.orientarSereno(este, [-16.429, -71.53], lectura(10));
    const imprecisa = cadMarcadores.orientarSereno(este, [-16.4298, -71.5297], lectura(30, 100));
    const vuelta = cadMarcadores.orientarSereno(este, [-16.429, -71.53], lectura(180));
    const reanuda = cadMarcadores.orientarSereno(vuelta, [-16.4287, -71.53], lectura(195));
    return { inicial: cadMarcadores.textoOrientacion(inicial), repetida: repetida === este, atrasada: atrasada === este,
      imprecisa: imprecisa.direccion, vuelta: vuelta.direccion, interrumpida: vuelta.interrumpida,
      texto: cadMarcadores.textoOrientacion(vuelta), reanuda: reanuda.direccion, recuperada: reanuda.interrumpida };
  });
  expect(resultado).toEqual({ inicial: 'Sin rumbo confirmado', repetida: true, atrasada: true, imprecisa: 2,
    vuelta: 2, interrumpida: true, texto: 'Este · último rumbo conocido', reanuda: 0, recuperada: false });
});

test('al llegar al caso conserva el rumbo y respeta las capas del mapa', async ({ page }) => {
  const { datos, modificar, vehiculo } = await preparar(page);
  const lat = Number(datos.lat), lon = Number(datos.lon) + 0.0002;
  await modificar({ lat: String(lat), lon: String(lon) });
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'E');
  const caso = '00000000-0000-0000-0000-000000000123';
  await page.evaluate(({ caso, lat, lon }) => {
    const el = document.createElement('li');
    Object.assign(el.dataset, { casoMapa: caso, origen: 'SOS', estado: 'En atención', ciudadano: 'Ana Ficticia', lat: String(lat), lon: String(lon), detalle: '/panel/central' });
    document.querySelector('[data-casos-mapa]').append(el);
  }, { caso, lat, lon });
  await modificar({ caso, disponible: 'false' });
  const conjunto = page.locator('#mapa .marcador-conjunto--vehiculo');
  await expect(conjunto.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'E');
  expect(await conjunto.locator('.camioneta__sprite').evaluate((el) => getComputedStyle(el).backgroundImage)).toContain('camioneta-gris-direcciones.png');
  await expect(conjunto.locator('.conjunto__etiqueta')).toHaveText('En el lugar');
  await page.evaluate(() => document.querySelector('.central').classList.add('central--sin-sereno'));
  await expect(conjunto.locator('.conjunto__sereno')).toBeHidden();
  await page.evaluate(() => document.querySelector('.central').classList.remove('central--sin-sereno'));
  await modificar({ caso: '', lat: String(lat + 0.001) });
  await expect(vehiculo.locator('[data-camioneta]')).toHaveAttribute('data-direccion', 'N');
});

test('el minimapa mantiene gris al asignado incluso al orientarlo con la ruta', async ({ page }) => {
  await prepararPagina(page);
  await page.route('**/prueba-minimapa-sprite', (ruta) => ruta.fulfill({ contentType: 'text/html', body: `<!doctype html><html lang="es"><head>
    <link rel="stylesheet" href="/css/tokens.css"><link rel="stylesheet" href="/css/panel.css"><link rel="stylesheet" href="/lib/leaflet/leaflet.css"></head><body>
    <div id="mapa-hecho" style="height:320px;width:320px" data-lat="-16.431" data-lon="-71.527" data-teselas="https://tile.openstreetmap.org/{z}/{x}/{y}.png"></div>
    <ul data-serenos-minimapa data-ruta='[[-16.431,-71.53],[-16.431,-71.529],[-16.431,-71.527]]'>
      <li data-sereno="prueba-gris" data-nombre="Sara Ficticia" data-disponible="false" data-asignado="true" data-lat="-16.431" data-lon="-71.53" data-distancia="320 m"></li>
    </ul><script src="/lib/leaflet/leaflet.js"></script><script src="/js/marcadores.js"></script><script src="/js/mapa-hecho.js"></script></body></html>` }));
  await page.goto('/prueba-minimapa-sprite');
  const camioneta = page.locator('#mapa-hecho [data-camioneta]');
  await expect(camioneta).toHaveClass(/camioneta--no-disponible/);
  await expect(camioneta).toHaveAttribute('data-direccion', 'E');
  expect(await camioneta.locator('.camioneta__sprite').evaluate((el) => getComputedStyle(el).backgroundImage)).toContain('camioneta-gris-direcciones.png');
});
