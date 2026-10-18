// Prototipo de la central «Mapa protagonista»: tres cargas de trabajo, selección con línea de despacho,
// asignación con confirmación, SOS nuevo en vivo y reordenación en el sitio. Datos ficticios.
import { abrirDialogo, actualizarIndicador, anunciar, barra, icono, mapaBase, marcador, reordenarConMovimiento, toast } from './prototipo.js';

const SERENOS = [
  { id: 's1', nombre: 'Sara Serena Ficticia', ini: 'SS', x: 1010, y: 300, estado: 'disponible', hace: 12 },
  { id: 's2', nombre: 'Luis Ficticio', ini: 'LF', x: 1360, y: 470, estado: 'disponible', hace: 20 },
  { id: 's3', nombre: 'Marta Ficticia', ini: 'MF', x: 900, y: 800, estado: 'atencion', hace: 9 },
  { id: 's4', nombre: 'Julia Ficticia', ini: 'JF', x: 1180, y: 860, estado: 'disponible', hace: 31 },
  { id: 's5', nombre: 'Pedro Suplente Ficticio', ini: 'PS', x: 0, y: 0, estado: 'no' },
];

const CASOS = {
  ana: { id: 'ana', tipo: 'sos', nombre: 'Ana Ficticia', hace: '42 s', lugar: 'Jr. Lampa 545 · ± 5 m', x: 960, y: 470, estado: 'Pendiente' },
  beto: { id: 'beto', tipo: 'urgente', nombre: 'Beto Ficticio', hace: '57 s', lugar: 'Desde un acompañamiento · «Necesito ayuda»', x: 1250, y: 300, estado: 'Pendiente' },
  carla: { id: 'carla', tipo: 'sos', nombre: 'Carla Ficticia', hace: '2 min', lugar: 'Av. Tacna 820 · ± 12 m', x: 760, y: 610, estado: 'Pendiente', nota: 'Contactada · falta clasificar' },
  diego: { id: 'diego', tipo: 'sos', nombre: 'Diego Ficticio', hace: '3 min', lugar: 'Jr. Huallaga 310 · ± 8 m', x: 1120, y: 520, estado: 'Pendiente' },
  elena: { id: 'elena', tipo: 'sos', nombre: 'Elena Ficticia', hace: '4 min', lugar: 'Sin ubicación del dispositivo', x: null, y: null, estado: 'Pendiente', nota: 'Pidió ayuda sin ubicación' },
  robo: { id: 'robo', tipo: 'reporte', nombre: 'Robo de celular', hace: '23:31', lugar: 'Av. Abancay · lugar del hecho', x: 1290, y: 620, estado: 'Por asignar' },
  luz: { id: 'luz', tipo: 'reporte', nombre: 'Luminaria apagada', hace: '23:18', lugar: 'Pasaje al costado del mercado central', x: 1460, y: 420, estado: 'Por asignar' },
};
const ACOMPANAMIENTOS = [{ id: 'a1', x: 640, y: 520 }, { id: 'a2', x: 1420, y: 760 }, { id: 'a3', x: 980, y: 900 }];

const ESCENARIOS = {
  tranquilo: { casos: [], acompanamientos: 2, ocupados: [] },
  normal: { casos: ['ana', 'robo'], acompanamientos: 2, ocupados: ['s3'] },
  saturado: { casos: ['ana', 'beto', 'carla', 'diego', 'elena', 'robo', 'luz'], acompanamientos: 3, ocupados: ['s2', 's3', 's4'] },
};

let estado = { escenario: 'normal', casos: [], serenos: [], elegido: null, pestana: 'todo', capas: { sos: true, reporte: true, acompanamiento: true, sereno: true } };

document.getElementById('barra').innerHTML = barra('central');

function cargar(nombre) {
  const e = ESCENARIOS[nombre];
  estado.escenario = nombre;
  estado.casos = e.casos.map((id) => ({ ...CASOS[id] }));
  estado.serenos = SERENOS.map((s) => ({ ...s, estado: e.ocupados.includes(s.id) ? (s.id === 's3' ? 'atencion' : 'camino') : s.estado }));
  estado.acompanamientos = ACOMPANAMIENTOS.slice(0, e.acompanamientos);
  estado.elegido = estado.casos.find((c) => c.tipo !== 'reporte')?.id ?? null;
  document.querySelectorAll('[data-escenario]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.escenario === nombre)));
  pintar();
}

const esSos = (c) => c.tipo === 'sos' || c.tipo === 'urgente';
const disponibles = () => estado.serenos.filter((s) => s.estado === 'disponible');
const distancia = (a, b) => Math.round(Math.hypot(a.x - b.x, a.y - b.y) * 1.6);
function masCercano(caso) {
  if (caso?.x == null) return null;
  return disponibles().map((s) => ({ s, d: distancia(caso, s) })).sort((a, b) => a.d - b.d)[0] ?? null;
}

function chipEstado(c) {
  if (c.estado === 'Asignada') return `<span class="chip chip--progreso">${icono('asignado')}Asignada</span>`;
  if (c.tipo === 'reporte') return `<span class="chip chip--advertencia">${icono('reloj')}Por asignar</span>`;
  return `<span class="chip chip--urgente">${icono('reloj')}Pendiente</span>`;
}

function tarjeta(c) {
  const elegido = c.id === estado.elegido;
  const cerca = masCercano(c);
  const cabecera = esSos(c)
    ? `<span class="etiqueta-sos">${icono('sirena')}SOS</span>${c.tipo === 'urgente' ? `<span class="chip chip--acompanamiento">${icono('ruta')}Acompañamiento</span>` : ''}`
    : `<span class="chip chip--reporte">${icono('documento')}Reporte</span>`;
  const acciones = elegido && c.estado !== 'Asignada' ? `
    <div class="caso__acciones">
      ${esSos(c) ? `<a class="boton boton--principal boton--compacto" href="sos.html">Verificar y contactar</a>` : `<a class="boton boton--principal boton--compacto" href="#">Ver el reporte</a>`}
      ${cerca ? `<button class="boton boton--secundario boton--compacto" type="button" data-asignar="${c.id}">Asignar a ${cerca.s.nombre.split(' ')[0]}</button>` : ''}
    </div>
    ${cerca ? `<p class="caso__sugerencia">Más cercano disponible: <b>${cerca.s.nombre}</b> · ${cerca.d} m</p>` : `<p class="caso__sugerencia">${c.x == null ? 'Sin ubicación: confirma la referencia al contactar.' : 'No hay serenos disponibles en este momento.'}</p>`}` : '';
  return `<li data-id="${c.id}">
    <article class="caso${esSos(c) && c.estado !== 'Asignada' ? ' caso--urgente' : ''}${elegido ? ' caso--elegido' : ''}${c.nuevo ? ' caso--nuevo' : ''}${c.cambio ? ' caso--cambio' : ''}" tabindex="0" data-elegir="${c.id}" aria-label="${esSos(c) ? 'SOS' : 'Reporte'} de ${c.nombre}, ${c.estado}">
      <div class="caso__fila">${cabecera}${chipEstado(c)}<span class="caso__hora">${c.tipo === 'reporte' ? c.hace : `hace ${c.hace}`}</span></div>
      <p class="caso__nombre">${c.nombre}</p>
      <p class="caso__dato">${icono(c.x == null ? 'advertencia' : 'ubicacion', 'icono icono--sm')}${c.nota ?? c.lugar}</p>
      ${acciones}
    </article></li>`;
}

function filtrados() {
  const orden = (c) => (c.estado === 'Asignada' ? 2 : esSos(c) ? 0 : 1);
  const lista = [...estado.casos].sort((a, b) => orden(a) - orden(b));
  if (estado.pestana === 'sos') return lista.filter(esSos);
  if (estado.pestana === 'reportes') return lista.filter((c) => c.tipo === 'reporte');
  return lista;
}

function pintarCola(animar = false) {
  const ul = document.getElementById('lista-casos');
  const aplicar = () => {
    const lista = filtrados();
    ul.innerHTML = lista.length ? lista.map(tarjeta).join('') : `<li class="vacio">${icono('exito')}<b>Sin SOS pendientes.</b>Los nuevos aparecerán aquí al instante, con aviso sonoro si está activado.</li>`;
  };
  animar ? reordenarConMovimiento(ul, aplicar) : aplicar();
  const sos = estado.casos.filter((c) => esSos(c) && c.estado !== 'Asignada').length;
  const rep = estado.casos.filter((c) => c.tipo === 'reporte').length;
  document.getElementById('dato-cola').textContent = `${estado.casos.length} casos`;
  document.querySelector('[data-pestana="sos"] b').textContent = sos;
  document.querySelector('[data-pestana="reportes"] b').textContent = rep;
  document.querySelector('[data-pestana="acompanamientos"] b').textContent = estado.acompanamientos.length;
  actualizarIndicador('sos', sos, true);
  actualizarIndicador('porAsignar', rep);
  actualizarIndicador('serenos', `${disponibles().length}/${estado.serenos.length}`);
  actualizarIndicador('acompanamientos', estado.acompanamientos.length);
}

function pintarSerenos() {
  const etiqueta = { disponible: ['exito', 'Disponible'], camino: ['progreso', 'En camino'], atencion: ['progreso', 'En atención'], asignado: ['progreso', 'Asignado'], no: ['neutro', 'No operativo'] };
  const cerca = masCercano(estado.casos.find((c) => c.id === estado.elegido));
  document.getElementById('dato-serenos').textContent = `${disponibles().length} de ${estado.serenos.length} disponibles`;
  document.getElementById('lista-serenos').innerHTML = estado.serenos.map((s) => {
    const [tono, texto] = etiqueta[s.estado];
    const dato = s.estado === 'no' ? 'Sin posición' : `Posición hace ${s.hace} s${cerca?.s.id === s.id ? ` · ${cerca.d} m del caso elegido` : ''}`;
    return `<li class="sereno${cerca?.s.id === s.id ? ' sereno--sugerido' : ''}"><span class="sereno__avatar${s.estado === 'no' ? ' sereno__avatar--no' : ''}">${s.ini}</span><span class="sereno__nombre">${s.nombre}</span><span class="chip chip--${tono}">${texto}</span><span class="sereno__dato">${dato}</span></li>`;
  }).join('');
}

function pintarMapa() {
  const capas = estado.capas;
  const caso = estado.casos.find((c) => c.id === estado.elegido);
  const cerca = masCercano(caso);
  const marcadores = [
    ...(capas.acompanamiento ? estado.acompanamientos.map((a) => marcador('acompanamiento', a.x, a.y, { id: a.id })) : []),
    ...(capas.reporte ? estado.casos.filter((c) => c.tipo === 'reporte').map((c) => marcador('reporte', c.x, c.y, { id: c.id })) : []),
    ...(capas.sereno ? estado.serenos.filter((s) => s.estado !== 'no').map((s) => marcador('sereno', s.x, s.y, { id: s.id, disponible: s.estado === 'disponible' })) : []),
    ...(capas.sos ? estado.casos.filter((c) => esSos(c) && c.x != null).map((c) => marcador('sos', c.x, c.y, { id: c.id, enCurso: c.estado === 'Asignada' })) : []),
  ];
  let despacho = '';
  if (caso && cerca && caso.estado !== 'Asignada') {
    const mx = (caso.x + cerca.s.x) / 2; const my = (caso.y + cerca.s.y) / 2;
    despacho = `<path class="linea-despacho" d="M${caso.x} ${caso.y} L${cerca.s.x} ${cerca.s.y}"/>
      <g class="etiqueta-despacho"><rect x="${mx - 38}" y="${my - 14}" width="76" height="28" rx="14"/><text x="${mx}" y="${my + 5}" text-anchor="middle">${cerca.d} m</text></g>`;
  }
  document.getElementById('marcadores').innerHTML = despacho + marcadores.join('');
  for (const c of estado.casos.filter((c) => esSos(c) && c.estado !== 'Asignada' && (c.nuevo || c.id === estado.elegido))) {
    document.querySelector(`[data-marcador="${c.id}"] .anillo-sos`)?.classList.add('anillo-sos--activo');
  }
  document.querySelector(`[data-marcador="${estado.elegido}"]`)?.classList.add('marcador--elegido');
}

function pintar(animar = false) { pintarCola(animar); pintarSerenos(); pintarMapa(); }

function elegir(id) {
  const caso = estado.casos.find((c) => c.id === id);
  if (!caso) return;
  caso.nuevo = false;
  estado.elegido = id;
  pintar();
  document.querySelector(`[data-elegir="${id}"]`)?.focus({ preventScroll: true });
}

function asignar(id) {
  const caso = estado.casos.find((c) => c.id === id);
  const cerca = masCercano(caso);
  const velo = document.getElementById('dialogo-asignar');
  velo.querySelector('#titulo-asignar').textContent = `¿Asignar este ${esSos(caso) ? 'SOS' : 'reporte'} a ${cerca.s.nombre}?`;
  velo.querySelector('[data-resumen]').innerHTML = `<span>${esSos(caso) ? `<span class="etiqueta-sos">SOS</span>` : `<span class="chip chip--reporte">Reporte</span>`}&nbsp; ${caso.nombre} · ${caso.lugar}</span>
    <span>${cerca.s.nombre.split(' ')[0]} está disponible a ${cerca.d} m y recibirá el despacho en su teléfono.</span>`;
  velo.querySelector('[data-confirmar]').textContent = `Asignar a ${cerca.s.nombre.split(' ')[0]}`;
  abrirDialogo(velo, {
    alConfirmar: () => {
      caso.estado = 'Asignada'; caso.cambio = true; caso.nuevo = false;
      cerca.s.estado = 'asignado';
      estado.elegido = estado.casos.find((c) => esSos(c) && c.estado !== 'Asignada')?.id ?? null;
      pintar(true);
      caso.cambio = false;
      toast(`<strong>${esSos(caso) ? 'SOS' : 'Reporte'} asignado</strong> a ${cerca.s.nombre}.`);
    },
  });
}

let siguiente = 0;
const NUEVOS = [
  { nombre: 'Fabiola Ficticia', lugar: 'Jr. Cuzco 640 · ± 6 m', x: 1080, y: 420 },
  { nombre: 'Gonzalo Ficticio', lugar: 'Av. Grau 1020 · ± 9 m', x: 1210, y: 700 },
  { nombre: 'Hilda Ficticia', lugar: 'Jr. Ancash 215 · ± 4 m', x: 980, y: 210 },
];
function simularSos() {
  const datos = NUEVOS[siguiente++ % NUEVOS.length];
  const caso = { id: `n${Date.now()}`, tipo: 'sos', hace: '1 s', estado: 'Pendiente', nuevo: true, ...datos };
  estado.casos.unshift(caso);
  if (estado.pestana === 'reportes') estado.pestana = 'todo';
  pintar(true);
  anunciar(`Nuevo SOS de ${caso.nombre}`, 'assertive');
}

document.addEventListener('click', (e) => {
  const asignarBtn = e.target.closest('[data-asignar]');
  if (asignarBtn) { e.stopPropagation(); asignar(asignarBtn.dataset.asignar); return; }
  if (e.target.closest('a')) return;
  const tarjetaEl = e.target.closest('[data-elegir]');
  if (tarjetaEl) { elegir(tarjetaEl.dataset.elegir); return; }
  const m = e.target.closest('[data-marcador]');
  if (m && estado.casos.some((c) => c.id === m.dataset.marcador)) { elegir(m.dataset.marcador); return; }
  const esc = e.target.closest('[data-escenario]');
  if (esc) cargar(esc.dataset.escenario);
  if (e.target.closest('[data-simular]')) simularSos();
  if (e.target.closest('[data-conexion-prueba]')) {
    const b = e.target.closest('[data-conexion-prueba]');
    const perdida = b.getAttribute('aria-pressed') !== 'true';
    b.setAttribute('aria-pressed', String(perdida));
    const c = document.querySelector('[data-conexion]');
    c.classList.toggle('conexion--perdida', perdida);
    c.lastChild.textContent = perdida ? 'Reconectando…' : 'En tiempo real';
    document.getElementById('aviso-conexion').hidden = !perdida;
  }
  const pest = e.target.closest('[data-pestana]');
  if (pest && pest.dataset.pestana !== 'acompanamientos') {
    estado.pestana = pest.dataset.pestana;
    document.querySelectorAll('[data-pestana]').forEach((p) => p.setAttribute('aria-selected', String(p === pest)));
    pintarCola(true);
  }
  const capa = e.target.closest('[data-capa]');
  if (capa) {
    estado.capas[capa.dataset.capa] = !estado.capas[capa.dataset.capa];
    capa.setAttribute('aria-pressed', String(estado.capas[capa.dataset.capa]));
    pintarMapa();
  }
  const plegar = e.target.closest('[data-plegar]');
  if (plegar) {
    const panel = document.getElementById(plegar.dataset.plegar);
    const clase = panel.id === 'panel-cola' ? 'cola--plegada' : 'serenos--plegado';
    const plegado = panel.classList.toggle(clase);
    plegar.setAttribute('aria-expanded', String(!plegado));
    plegar.innerHTML = icono(plegado === (panel.id === 'panel-cola') ? 'derecha' : 'izquierda');
  }
});

document.addEventListener('keydown', (e) => {
  const tarjetaEl = e.target.closest?.('[data-elegir]');
  if (tarjetaEl && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); elegir(tarjetaEl.dataset.elegir); }
  if (['j', 'k'].includes(e.key) && !e.target.closest('input, textarea')) {
    const lista = filtrados(); const i = lista.findIndex((c) => c.id === estado.elegido);
    const otro = lista[Math.max(0, Math.min(lista.length - 1, i + (e.key === 'j' ? 1 : -1)))];
    if (otro) elegir(otro.id);
  }
});

document.getElementById('mapa').innerHTML = `<svg viewBox="0 0 1600 1000" preserveAspectRatio="xMidYMid slice" role="img" aria-label="Mapa operativo del centro de Lima">${mapaBase()}<g id="marcadores"></g></svg>`;
cargar('normal');
