// Herramientas de demostración (05/11/2026): lo común a sembrar.mjs y mover-serenos.mjs.
// Solo usan lo que usan la app y el panel: la API pública (/api/...) y los formularios del panel con su token antifalsificación.
// Así funcionan igual contra el servidor local que contra Render. Node 20 o superior, sin dependencias.
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';

// ---------- Configuración (variables de entorno o un archivo JSON) ----------
// CAD_URL: dirección del servidor (por defecto, el local de la revisión visual).
// CAD_CREDENCIALES: archivo JSON { "operador": { "correo", "clave" }, "serenos": [ { "correo", "clave" }, ... ] }.
// O bien CAD_OPERADOR="correo:clave" y CAD_SERENOS="correo:clave;correo:clave".
export function configuracion() {
  const url = (process.env.CAD_URL ?? 'http://localhost:5240').replace(/\/+$/, '');
  let operador = null;
  let serenos = [];
  if (process.env.CAD_CREDENCIALES) {
    const datos = JSON.parse(readFileSync(process.env.CAD_CREDENCIALES, 'utf8'));
    operador = datos.operador ?? null;
    serenos = datos.serenos ?? [];
  }
  const par = (texto) => {
    const i = texto.indexOf(':');
    return i < 0 ? null : { correo: texto.slice(0, i).trim(), clave: texto.slice(i + 1) };
  };
  if (process.env.CAD_OPERADOR) operador = par(process.env.CAD_OPERADOR);
  if (process.env.CAD_SERENOS) serenos = process.env.CAD_SERENOS.split(';').map(par).filter(Boolean);
  return { url, operador, serenos };
}

export const ahora = () => new Date().toISOString();
export const esperar = (ms) => new Promise((r) => setTimeout(r, ms));
export const nuevoId = () => randomUUID();

// ---------- API pública (app) ----------
export function crearApi(url) {
  async function api(ruta, { cuerpo, token, metodo = cuerpo === undefined ? 'GET' : 'POST' } = {}) {
    const respuesta = await fetch(url + ruta, {
      method: metodo,
      headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
    });
    const texto = await respuesta.text();
    if (!respuesta.ok) throw new Error(`${metodo} ${ruta} respondió ${respuesta.status}: ${texto.slice(0, 300)}`);
    return texto ? JSON.parse(texto) : null;
  }
  const token = async (correo, clave) => (await api('/api/auth/token', { cuerpo: { correo, contrasena: clave } })).token;
  return { api, token };
}

// ---------- Panel (formularios con cookie de sesión y token antifalsificación) ----------
export function crearPanel(url) {
  const cookies = new Map();
  const guardar = (respuesta) => {
    const lista = typeof respuesta.headers.getSetCookie === 'function' ? respuesta.headers.getSetCookie() : [respuesta.headers.get('set-cookie')].filter(Boolean);
    for (const c of lista) {
      const [par] = c.split(';');
      const i = par.indexOf('=');
      cookies.set(par.slice(0, i).trim(), par.slice(i + 1));
    }
  };
  const cabecera = () => [...cookies].map(([k, v]) => `${k}=${v}`).join('; ');

  async function obtener(ruta) {
    const respuesta = await fetch(url + ruta, { headers: { Cookie: cabecera(), Accept: 'text/html' }, redirect: 'manual' });
    guardar(respuesta);
    return { estado: respuesta.status, html: await respuesta.text(), location: respuesta.headers.get('location') };
  }

  const tokenDe = (html) => html.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/)?.[1]
    ?? html.match(/value="([^"]+)"[^>]*name="__RequestVerificationToken"/)?.[1];

  // Envía un formulario de la página «ruta» (con su token); «handler» es el manejador de Razor Pages (o null).
  async function enviar(ruta, handler, campos = {}) {
    const pagina = await obtener(ruta);
    const token = tokenDe(pagina.html);
    if (!token) throw new Error(`No hay token antifalsificación en ${ruta} (¿sesión caducada o sin permiso?).`);
    const cuerpo = new URLSearchParams({ ...campos, __RequestVerificationToken: token });
    const destino = handler ? `${ruta}${ruta.includes('?') ? '&' : '?'}handler=${handler}` : ruta;
    const respuesta = await fetch(url + destino, {
      method: 'POST', redirect: 'manual', body: cuerpo,
      headers: { Cookie: cabecera(), 'Content-Type': 'application/x-www-form-urlencoded' },
    });
    guardar(respuesta);
    const html = await respuesta.text();
    // PRG: una acción registrada redirige; una página de vuelta (200) trae el error del formulario.
    if (respuesta.status !== 302 && respuesta.status !== 303) {
      const error = html.match(/class="campo__error"[^>]*>[\s\S]*?<span>([^<]+)<\/span>/)?.[1] ?? html.match(/class="aviso__texto">([^<]+)</)?.[1] ?? `respuesta ${respuesta.status}`;
      throw new Error(`${destino}: ${error.trim()}`);
    }
    return respuesta.headers.get('location');
  }

  async function ingresar(correo, clave) {
    const destino = await enviar('/panel/ingresar', null, { 'Formulario.Correo': correo, 'Formulario.Contrasena': clave });
    if (!destino || destino.includes('/ingresar')) throw new Error('No se pudo ingresar al panel con esa cuenta de operador.');
  }

  // Valores de un <select> de una página (catálogos del servidor: prioridades, destinos, categorías…).
  const opciones = (html, nombre) => {
    const bloque = html.match(new RegExp(`<select[^>]*name="${nombre.replace('.', '\\.')}"[^>]*>([\\s\\S]*?)</select>`))?.[1] ?? '';
    return [...bloque.matchAll(/<option[^>]*value="([^"]+)"[^>]*>([^<]*)</g)].map((m) => ({ valor: m[1], texto: m[2].trim() }));
  };

  return { obtener, enviar, ingresar, opciones };
}

// ---------- Geografía ----------
const METROS_POR_GRADO = 111_320;
export function desplazar({ latitud, longitud }, norteMetros, esteMetros) {
  return {
    latitud: latitud + norteMetros / METROS_POR_GRADO,
    longitud: longitud + esteMetros / (METROS_POR_GRADO * Math.cos((latitud * Math.PI) / 180)),
  };
}
export function distancia(a, b) {
  const dy = (b.latitud - a.latitud) * METROS_POR_GRADO;
  const dx = (b.longitud - a.longitud) * METROS_POR_GRADO * Math.cos((a.latitud * Math.PI) / 180);
  return Math.hypot(dx, dy);
}
// Un punto al azar a menos de «radio» metros del centro.
export function alrededor(centro, radio) {
  const r = radio * Math.sqrt(Math.random());
  const angulo = Math.random() * 2 * Math.PI;
  return desplazar(centro, r * Math.cos(angulo), r * Math.sin(angulo));
}

// Centro del mapa de la central (lo que configuró el despliegue), leído de la propia página.
export async function centroDelMapa(panel) {
  const { html } = await panel.obtener('/panel/central');
  const lat = Number(html.match(/data-centro-lat="([-0-9.,]+)"/)?.[1]?.replace(',', '.'));
  const lon = Number(html.match(/data-centro-lon="([-0-9.,]+)"/)?.[1]?.replace(',', '.'));
  if (!Number.isFinite(lat) || !Number.isFinite(lon)) throw new Error('No se pudo leer el centro del mapa de la central.');
  return { latitud: lat, longitud: lon };
}
