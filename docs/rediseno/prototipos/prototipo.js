// Utilidades comunes de los prototipos CAD v2: iconos, barra superior, mapa, toasts y diálogo accesible.
// Datos ficticios. Ningún prototipo se conecta al servidor.

const TRAZOS = {
  escudo: '<path d="M12 2.5 19.5 5.5v6c0 4.8-3.2 8.8-7.5 10-4.3-1.2-7.5-5.2-7.5-10v-6z"/><path d="m8.75 12 2.25 2.25 4.25-4.5"/>',
  sirena: '<path d="M6 18v-5a6 6 0 0 1 12 0v5"/><path d="M4 18h16v3H4zM12 2v2M4.2 5.2l1.4 1.4M19.8 5.2l-1.4 1.4"/>',
  ubicacion: '<path d="M12 21s-7-6.2-7-11.5a7 7 0 0 1 14 0C19 14.8 12 21 12 21z"/><circle cx="12" cy="9.5" r="2.5"/>',
  exito: '<circle cx="12" cy="12" r="9"/><path d="m8 12.25 2.75 2.75 5.25-5.5"/>',
  error: '<circle cx="12" cy="12" r="9"/><path d="m15 9-6 6M9 9l6 6"/>',
  informacion: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5.5M12 7.75h.01"/>',
  advertencia: '<path d="M10.3 4.2 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0z"/><path d="M12 9.5v4M12 17h.01"/>',
  reloj: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  bandeja: '<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1z"/>',
  lupa: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
  asignado: '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="m16 11 2 2 4-4"/>',
  camino: '<path d="m3 11 19-9-9 19-2-8-8-2z"/>',
  atencion: '<path d="M12 2.5 19.5 5.5v6c0 4.8-3.2 8.8-7.5 10-4.3-1.2-7.5-5.2-7.5-10v-6z"/>',
  derivar: '<path d="M7 17 17 7M7 7h10v10"/>',
  cancelar: '<circle cx="12" cy="12" r="9"/><path d="m4.9 4.9 14.2 14.2"/>',
  ruta: '<circle cx="6" cy="19" r="3"/><path d="M9 19h8.5a3.5 3.5 0 0 0 0-7h-11a3.5 3.5 0 0 1 0-7H15"/><circle cx="18" cy="5" r="3"/>',
  documento: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/>',
  telefono: '<path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z"/>',
  izquierda: '<path d="m15 18-6-6 6-6"/>',
  derecha: '<path d="m9 18 6-6-6-6"/>',
  copiar: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
  filtro: '<path d="M22 3H2l8 9.46V19l4 2v-8.54z"/>',
  hecho: '<path d="m5 12 5 5 9-10"/>',
};

export function icono(nombre, clase = 'icono', grosor = 2) {
  return `<svg class="${clase}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="${grosor}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">${TRAZOS[nombre] ?? ''}</svg>`;
}

const SECCIONES = [
  ['central', 'Central', 'central.html'],
  ['reportes', 'Reportes', '#'],
  ['casos', 'Casos', 'casos.html'],
  ['acompanamientos', 'Acompañamientos', '#'],
  ['concentracion', 'Concentración', '#'],
];

// Barra superior del panel. Los indicadores se actualizan con actualizarIndicador().
export function barra(actual, indicadores = {}) {
  const ind = (clave, texto, valor, urgente = false) =>
    `<span class="indicador${urgente && valor > 0 ? ' indicador--urgente' : ''}${valor === 0 ? ' indicador--cero' : ''}" data-indicador="${clave}">${texto}<b>${valor}</b></span>`;
  return `
  <header class="barra">
    <button class="barra__menu" type="button" aria-label="Secciones del panel" aria-expanded="false" onclick="const b=this.closest('.barra');const a=b.classList.toggle('barra--abierta');this.setAttribute('aria-expanded',a)"><svg class="icono" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M4 6h16M4 12h16M4 18h16"/></svg></button>
    <a class="barra__marca" href="index.html" aria-label="Central CAD, índice de prototipos">
      <span class="barra__escudo">${icono('escudo')}</span>
      <span class="barra__nombre"><b>Central CAD</b><small>MVP académico · datos simulados</small></span>
    </a>
    <nav aria-label="Secciones del panel"><ul class="barra__secciones">
      ${SECCIONES.map(([id, texto, href]) => `<li><a class="barra__seccion" href="${href}"${id === actual ? ' aria-current="page"' : ''}>${texto}</a></li>`).join('')}
    </ul></nav>
    <div class="barra__indicadores" aria-label="Resumen del turno">
      ${ind('sos', 'SOS', indicadores.sos ?? 0, true)}
      ${ind('porAsignar', 'Por asignar', indicadores.porAsignar ?? 0)}
      ${ind('serenos', 'Serenos libres', indicadores.serenos ?? '0')}
      ${ind('acompanamientos', 'Acompañamientos', indicadores.acompanamientos ?? 0)}
    </div>
    <div class="barra__persona"><span class="barra__avatar" aria-hidden="true">OO</span><span class="barra__persona-texto"><b>Olga Operadora Ficticia</b><span class="conexion" data-conexion><i></i>En tiempo real</span></span></div>
  </header>`;
}

export function actualizarIndicador(clave, valor, urgente = false) {
  const el = document.querySelector(`[data-indicador="${clave}"]`);
  if (!el) return;
  const cifra = el.querySelector('b');
  if (cifra.textContent === String(valor)) return;
  cifra.textContent = valor;
  cifra.classList.remove('cambia'); void cifra.offsetWidth; cifra.classList.add('cambia');
  el.classList.toggle('indicador--urgente', urgente && Number(valor) > 0);
  el.classList.toggle('indicador--cero', Number(valor) === 0);
}

export function toast(html, duracion = 4000) {
  let pila = document.querySelector('.toasts');
  if (!pila) {
    pila = Object.assign(document.createElement('div'), { className: 'toasts' });
    pila.setAttribute('role', 'status');
    pila.setAttribute('aria-live', 'polite');
    document.body.append(pila);
  }
  const t = Object.assign(document.createElement('div'), { className: 'toast', innerHTML: `${icono('exito')}<span>${html}</span>` });
  pila.append(t);
  setTimeout(() => { t.classList.add('saliendo'); t.addEventListener('animationend', () => t.remove(), { once: true }); }, duracion);
}

// Anuncio para lectores de pantalla (SOS nuevos: assertive).
export function anunciar(texto, urgencia = 'polite') {
  let region = document.getElementById(`anuncio-${urgencia}`);
  if (!region) {
    region = Object.assign(document.createElement('div'), { id: `anuncio-${urgencia}`, className: 'visualmente-oculto' });
    region.setAttribute('aria-live', urgencia);
    document.body.append(region);
  }
  region.textContent = '';
  setTimeout(() => { region.textContent = texto; }, 50);
}

// Diálogo modal: foco atrapado, Esc cancela, el foco vuelve a quien lo abrió.
export function abrirDialogo(velo, { alConfirmar } = {}) {
  const origen = document.activeElement;
  velo.hidden = false;
  const enfocables = () => [...velo.querySelectorAll('button, [href], input, select, textarea')].filter((e) => !e.disabled);
  enfocables().at(-1)?.focus();
  const cerrar = () => {
    velo.hidden = true;
    velo.removeEventListener('keydown', teclado);
    velo.removeEventListener('click', clic);
    origen?.focus?.();
  };
  const teclado = (e) => {
    if (e.key === 'Escape') cerrar();
    if (e.key === 'Tab') {
      const lista = enfocables(); const primero = lista[0]; const ultimo = lista.at(-1);
      if (e.shiftKey && document.activeElement === primero) { e.preventDefault(); ultimo.focus(); }
      else if (!e.shiftKey && document.activeElement === ultimo) { e.preventDefault(); primero.focus(); }
    }
  };
  const clic = (e) => {
    if (e.target === velo || e.target.closest('[data-cancelar]')) cerrar();
    if (e.target.closest('[data-confirmar]')) { cerrar(); alConfirmar?.(); }
  };
  velo.addEventListener('keydown', teclado);
  velo.addEventListener('click', clic);
  return cerrar;
}

// Reordena hijos animando su posición (FLIP): lo que cambia se mueve en su sitio.
export function reordenarConMovimiento(contenedor, aplicarCambio) {
  const antes = new Map([...contenedor.children].map((el) => [el.dataset.id, el.getBoundingClientRect()]));
  aplicarCambio();
  if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
  for (const el of contenedor.children) {
    const previo = antes.get(el.dataset.id);
    if (!previo) continue;
    const ahora = el.getBoundingClientRect();
    const dy = previo.top - ahora.top;
    if (Math.abs(dy) < 1) continue;
    el.animate([{ transform: `translateY(${dy}px)` }, { transform: 'none' }], { duration: 360, easing: 'cubic-bezier(0.16, 1, 0.3, 1)' });
  }
}

// Mapa cartográfico simplificado del centro de Lima (dibujado, sin teselas).
export function mapaBase() {
  const calles = [];
  for (let x = -200; x < 1800; x += 64) calles.push(`<path d="M${x} -100 L${x + 120} 1100"/>`);
  for (let y = -100; y < 1100; y += 58) calles.push(`<path d="M-100 ${y} L1700 ${y - 90}"/>`);
  return `
    <defs><clipPath id="recorte"><rect width="1600" height="1000"/></clipPath></defs>
    <g clip-path="url(#recorte)">
      <rect width="1600" height="1000" fill="var(--mapa-suelo)"/>
      <g stroke="var(--mapa-calle)" stroke-width="5" fill="none" opacity="0.9">${calles.join('')}</g>
      <path d="M-50 150 C 250 90, 420 210, 700 150 S 1150 70, 1650 150" stroke="var(--mapa-rio)" stroke-width="58" fill="none" stroke-linecap="round"/>
      <rect x="1080" y="560" width="190" height="130" rx="14" fill="var(--mapa-parque)" transform="rotate(-4 1175 625)"/>
      <rect x="560" y="700" width="120" height="90" rx="12" fill="var(--mapa-parque)" transform="rotate(-4 620 745)"/>
      <g fill="none" stroke-linecap="round">
        <path d="M-50 420 L1650 330" stroke="var(--mapa-borde-avenida)" stroke-width="22"/><path d="M-50 420 L1650 330" stroke="var(--mapa-avenida)" stroke-width="18"/>
        <path d="M-50 760 L1650 650" stroke="var(--mapa-borde-avenida)" stroke-width="22"/><path d="M-50 760 L1650 650" stroke="var(--mapa-avenida)" stroke-width="18"/>
        <path d="M760 -50 L900 1050" stroke="var(--mapa-borde-avenida)" stroke-width="22"/><path d="M760 -50 L900 1050" stroke="var(--mapa-avenida)" stroke-width="18"/>
        <path d="M1210 -50 L1330 1050" stroke="var(--mapa-borde-avenida)" stroke-width="22"/><path d="M1210 -50 L1330 1050" stroke="var(--mapa-avenida)" stroke-width="18"/>
      </g>
      <text class="mapa__nombre" x="960" y="96">Rímac</text>
      <text class="mapa__nombre" x="1010" y="260">Cercado de Lima</text>
      <text class="mapa__nombre" x="520" y="590">Breña</text>
      <text class="mapa__nombre" x="1380" y="860">La Victoria</text>
      <text class="mapa__via" x="1000" y="395" transform="rotate(-3 1000 395)">Av. Nicolás de Piérola</text>
      <text class="mapa__via" x="1000" y="700" transform="rotate(-3.7 1000 700)">Av. Grau</text>
      <text class="mapa__via" x="808" y="520" transform="rotate(82 808 520)">Av. Tacna</text>
      <text class="mapa__via" x="1262" y="520" transform="rotate(84 1262 520)">Av. Abancay</text>
      <text class="mapa__nombre" x="1110" y="630" style="text-transform:none;letter-spacing:0">Parque de la Exposición</text>
    </g>`;
}

// Marcadores. Las formas distinguen la clase de entidad; el color, su estado.
export function marcador(tipo, x, y, { id = '', enCurso = false, disponible = true } = {}) {
  if (tipo === 'sos' || tipo === 'urgente') {
    const relleno = enCurso ? '#fff' : 'var(--mapa-sos)';
    const trazo = enCurso ? 'var(--mapa-sos)' : '#fff';
    return `<g class="marcador" data-marcador="${id}" tabindex="-1">
      <circle class="anillo-sos" cx="${x}" cy="${y}" r="26"/>
      <rect x="${x - 20}" y="${y - 18}" width="40" height="36" rx="8" fill="${relleno}" stroke="${trazo}" stroke-width="4"/>
      <g transform="translate(${x - 10} ${y - 10}) scale(0.84)" stroke="${enCurso ? 'var(--mapa-sos)' : '#fff'}" fill="none" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">${TRAZOS.sirena}</g>
    </g>`;
  }
  if (tipo === 'reporte') {
    return `<g class="marcador" data-marcador="${id}"><circle cx="${x}" cy="${y}" r="15" fill="${enCurso ? '#fff' : 'var(--mapa-reporte)'}" stroke="${enCurso ? 'var(--mapa-reporte)' : '#fff'}" stroke-width="4"/></g>`;
  }
  if (tipo === 'acompanamiento') {
    return `<g class="marcador" data-marcador="${id}"><path d="M${x - 70} ${y + 30} l26 -12 l22 18 l22 -16" stroke="var(--mapa-acompanamiento)" stroke-width="4" fill="none" stroke-linecap="round" stroke-dasharray="1 0"/><circle cx="${x}" cy="${y}" r="12" fill="var(--mapa-acompanamiento)" stroke="#fff" stroke-width="4"/></g>`;
  }
  // Sereno: escudo con anillo blanco.
  return `<g class="marcador" data-marcador="${id}"><path d="M${x} ${y - 20} l18 8v10c0 10-8 17-18 20-10-3-18-10-18-20v-10z" fill="${disponible ? 'var(--mapa-sereno)' : 'var(--mapa-sereno-no-disponible)'}" stroke="#fff" stroke-width="4" stroke-linejoin="round"/></g>`;
}
