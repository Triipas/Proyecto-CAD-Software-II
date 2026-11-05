// Serenos que se mueven solos, para la demostración (05/11/2026). Cada sereno de CAD_SERENOS queda operativo y envía su
// posición cada CAD_INTERVALO segundos (3 por defecto), como la app: patrulla en torno a su punto de partida y, si tiene un
// caso asignado, va hacia él. Con CAD_AVANZAR=1 también marca «En camino» al salir y «En atención» al llegar.
//
//   CAD_URL=https://<tu-servicio>.onrender.com CAD_SERENOS="sereno1@...:clave;sereno2@...:clave" node herramientas/demo/mover-serenos.mjs
//
// Ctrl+C lo detiene (los serenos quedan operativos en su última posición).
import { ahora, alrededor, configuracion, crearApi, desplazar, distancia, esperar } from './comun.mjs';

const { url, serenos } = await configuracion({ pedirSerenos: true });
if (!serenos.length) {
  console.error('Faltan los serenos: CAD_SERENOS="correo:clave;correo:clave" (o CAD_CREDENCIALES). Ver herramientas/demo/README.md.');
  process.exit(1);
}
const intervalo = Number(process.env.CAD_INTERVALO ?? 3) * 1000;
const velocidad = Number(process.env.CAD_VELOCIDAD ?? 7); // metros por segundo (a pie rápido o en bicicleta)
const avanzar = process.env.CAD_AVANZAR === '1';
const centro = process.env.CAD_CENTRO ? (([la, lo]) => ({ latitud: Number(la), longitud: Number(lo) }))(process.env.CAD_CENTRO.split(',')) : null;
const { api, token } = crearApi(url);

const moviles = [];
for (const s of serenos) {
  const t = await token(s.correo, s.clave);
  await api('/api/sereno/estado', { metodo: 'PUT', token: t, cuerpo: { operativo: true, ubicacionAutorizada: true } });
  moviles.push({ correo: s.correo, token: t, punto: null, base: null, destino: null });
}
console.log(`${moviles.length} serenos en marcha contra ${url} (cada ${intervalo / 1000} s, ${velocidad} m/s). Ctrl+C para detener.`);

let vuelta = 0;
for (;;) {
  for (const m of moviles) {
    try {
      // Punto de partida: cerca de CAD_CENTRO (el centro del mapa de la central); sin él, el centro por defecto del panel (Arequipa).
      if (!m.punto) {
        m.base = centro ? alrededor(centro, 1200) : alrededor({ latitud: -16.3988, longitud: -71.5369 }, 1200);
        m.punto = m.base;
      }
      // Cada pocas vueltas se mira si tiene un caso asignado: entonces va hacia él.
      if (vuelta % 3 === 0) {
        const despacho = await api('/api/sereno/despacho', { token: m.token }).catch(() => null);
        const lugar = despacho?.ubicacion ?? (despacho?.lugarHecho?.latitud != null ? despacho.lugarHecho : null);
        // Se conserva el paso que ya dio este guion (el despacho puede tardar un instante en reflejarlo).
        const previo = m.caso && despacho && m.caso.id === despacho.casoId ? m.caso.estado : null;
        m.caso = despacho && lugar ? { id: despacho.casoId, estado: previo === 'En atención' ? previo : despacho.estado, punto: { latitud: lugar.latitud, longitud: lugar.longitud } } : null;
        if (m.caso && avanzar && m.caso.estado === 'Asignada') {
          await api(`/api/sereno/casos/${m.caso.id}/desplazamiento`, { token: m.token, metodo: 'POST' }); // si falla, se reintenta luego
          m.caso.estado = 'En camino';
          console.log(`  ${m.correo}: en camino al caso`);
        }
      }
      const objetivo = m.caso?.punto ?? (m.destino ??= alrededor(m.base, 600));
      const falta = distancia(m.punto, objetivo);
      const paso = velocidad * (intervalo / 1000);
      if (falta <= paso) {
        m.punto = objetivo;
        if (m.caso) {
          if (avanzar && m.caso.estado === 'En camino') {
            // Sin .catch: si el servidor no la registra, el error llega al manejador de abajo y se reintenta en la próxima vuelta.
            await api(`/api/sereno/casos/${m.caso.id}/atencion`, { token: m.token, metodo: 'POST' });
            m.caso.estado = 'En atención';
            console.log(`  ${m.correo}: llegó e inició la atención`);
          }
        } else m.destino = null; // patrulla: otro punto
      } else {
        const f = paso / falta;
        const norte = (objetivo.latitud - m.punto.latitud) * 111_320 * f;
        const este = (objetivo.longitud - m.punto.longitud) * 111_320 * Math.cos((m.punto.latitud * Math.PI) / 180) * f;
        m.punto = desplazar(m.punto, norte, este);
      }
      await api('/api/sereno/posiciones', { token: m.token, cuerpo: { ...m.punto, precisionMetros: 6, obtenidaEn: ahora() } });
    } catch (error) {
      console.log(`  ${m.correo}: ${error.message}`);
    }
  }
  vuelta++;
  await esperar(intervalo);
}
