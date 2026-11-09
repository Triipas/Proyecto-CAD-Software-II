// Analítica, «Comparación por bloques» (10/11/2026): los radiales, el reparto, las fichas y la evolución de 12 meses, con el filtro en
// dos pasos (bloque y, al elegirlo, sus zonas) y el indicador. Los conteos llegan del servidor en [data-datos-bloques] (zonas con
// pocos casos ya sumadas en su bloque); aquí solo se filtran y se dibujan. La selección queda en la dirección (?bloque=&zonas=…) y
// viaja con los enlaces del periodo. Las formas cambian con una transición de su trazado (d); con movimiento reducido, sin ella.
(() => {
  const nodoDatos = document.querySelector("[data-datos-bloques]");
  if (!nodoDatos) return;
  const D = JSON.parse(nodoDatos.textContent);
  const EJES = D.ejes;
  const N = EJES.length;
  const TOTAL = N; // en cada conteo, después de los ejes va el total (con «Otros» y los sin clasificar)

  // Color por bloque: el mismo en cada gráfico (el color sigue al bloque, nunca a su puesto). Validados en conjunto: sobre el
  // panel de noche, los tonos «oscuro»; sobre blanco, los «claro» (siempre con su nombre al lado).
  const COLORES = {
    bustamante: { oscuro: "#3987e5", claro: "#2a78d6", rgb: "42 120 214" },
    avelino: { oscuro: "#d95926", claro: "#eb6834", rgb: "235 104 52" },
    simon: { oscuro: "#199e70", claro: "#1baf7a", rgb: "27 175 122" },
  };
  // Nombres cortos para los rótulos al final de las líneas (el nombre completo va en la leyenda).
  const CORTOS_BLOQUE = { bustamante: "Bustamante", avelino: "Avelino", simon: "Simón Bolívar" };
  const GRIS_NOCHE = "#9DB1D0";
  const GRIS = "#7487A2";
  const MAX_ZONAS_RADIAL = 6;
  const MAX_ZONAS_REPARTO = 4;
  const ZONA_PARA_ESCALA = 20; // casos desde los que una zona fija la escala de los radiales de zona
  const TONOS_ZONA = [1, 0.72, 0.5, 0.32];
  const R = 138; // radio de los radiales en un recuadro de 560 × 460: deja lugar a los lados para las etiquetas
  const NS = "http://www.w3.org/2000/svg";

  const cero = () => new Array(N + 1).fill(0);
  const sumar = (a, b) => a.map((x, i) => x + b[i]);
  const restar = (a, b) => a.map((x, i) => x - b[i]);
  const ceroMeses = () => D.meses.map(() => cero());
  const sumarMeses = (a, b) => a.map((m, i) => sumar(m, b[i]));
  const sumaEjes = (v) => v.slice(0, N).reduce((s, x) => s + x, 0);
  const perfil = (v) => { const t = sumaEjes(v) || 1; return v.slice(0, N).map((x) => (100 * x) / t); };
  const e = (texto) => String(texto).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
  const f1 = (n) => n.toFixed(1);
  const pct = (n) => `${n.toFixed(1).replace(".", ",")} %`;

  const bloques = D.bloques.map((b) => ({ ...b, color: COLORES[b.clave] ?? { oscuro: GRIS_NOCHE, claro: GRIS, rgb: "116 135 162" } }));
  const porClave = Object.fromEntries(bloques.map((b) => [b.clave, b]));
  const zonas = new Map();
  for (const b of bloques) for (const z of b.zonas) zonas.set(z.nombre, { ...z, bloque: b });
  const distrito = bloques.reduce((s, b) => sumar(s, b.casos), cero());

  // ---------- Estado (en la dirección) ----------
  const q = new URLSearchParams(location.search);
  const estado = {
    bloque: porClave[q.get("bloque")] ? q.get("bloque") : "todos",
    zonas: [],
    indicador: q.get("indicador") === "todos" || EJES.some((x) => x.clave === q.get("indicador")) ? q.get("indicador") : "robo",
    vista: q.get("vista") === "comparar" ? "comparar" : "bloques",
    escala: q.get("escala") === "cantidad" ? "cantidad" : "perfil",
    comparar: bloques.map((b) => b.clave),
    mes: null,
  };
  if (estado.bloque !== "todos")
    estado.zonas = (q.get("zonas") ?? "").split(",").filter((z) => zonas.get(z)?.bloque.clave === estado.bloque);

  const elegido = () => porClave[estado.bloque] ?? null;
  const conZonas = () => !!elegido() && estado.zonas.length > 0;
  const k = () => EJES.findIndex((x) => x.clave === estado.indicador);
  const valor = (v) => (k() < 0 ? v[TOTAL] : v[k()]);
  const nombreIndicador = () => (k() < 0 ? "Todos los casos" : EJES[k()].nombre);
  const casosDe = (n) => `${n} ${n === 1 ? "caso" : "casos"}${k() < 0 ? "" : ` de ${EJES[k()].nombre.toLowerCase()}`}`;
  const deZonas = (lista) => lista.reduce((s, z) => sumar(s, zonas.get(z).casos), cero());
  const mesesDeZonas = (lista) => lista.reduce((s, z) => sumarMeses(s, zonas.get(z).meses), ceroMeses());
  const seleccion = () => (!elegido() ? distrito : conZonas() ? deZonas(estado.zonas) : elegido().casos);

  // ---------- Elementos ----------
  const $ = (selector) => document.querySelector(selector);
  const titular = $("[data-titular]");
  const filaBloque = $('[data-fila="bloque"]');
  const filaZonas = $('[data-fila="zonas"]');
  const contenedorZonas = $("[data-fila-zonas]");
  const filaIndicador = $('[data-fila="indicador"]');
  const panel = $("[data-panel-perfil]");

  // ---------- Acciones (un solo oyente para todos los botones de la sección) ----------
  const acciones = {
    bloque: (v) => { estado.bloque = estado.bloque === v && v !== "todos" ? "todos" : v; estado.zonas = []; },
    zona: (v) => { estado.zonas = estado.zonas.includes(v) ? estado.zonas.filter((z) => z !== v) : [...estado.zonas, v]; },
    "todas-las-zonas": () => { estado.zonas = []; },
    indicador: (v) => { estado.indicador = v; },
    vista: (v) => { estado.vista = v; },
    escala: (v) => { estado.escala = v; },
    comparar: (v) => {
      if (!estado.comparar.includes(v)) estado.comparar = [...estado.comparar, v];
      else if (estado.comparar.length > 1) estado.comparar = estado.comparar.filter((c) => c !== v);
    },
  };
  document.querySelector(".analitica__trabajo").addEventListener("click", (ev) => {
    const boton = ev.target.closest("[data-accion]");
    if (!boton || !acciones[boton.dataset.accion]) return;
    acciones[boton.dataset.accion](boton.dataset.valor);
    dibujar();
    guardar();
  });

  // ---------- Geometría de los radiales ----------
  const angulo = (i) => ((-90 + (i * 360) / N) * Math.PI) / 180;
  const punto = (i, r) => [Math.cos(angulo(i)) * r, Math.sin(angulo(i)) * r];

  // Curva cerrada que pasa por los puntos (Catmull-Rom): la forma se lee como un perfil, no como una estrella de picos.
  function cerrada(pts) {
    const n = pts.length;
    let d = `M${f1(pts[0][0])} ${f1(pts[0][1])}`;
    for (let i = 0; i < n; i++) {
      const p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
      d += ` C${f1(p1[0] + (p2[0] - p0[0]) / 6)} ${f1(p1[1] + (p2[1] - p0[1]) / 6)} ${f1(p2[0] - (p3[0] - p1[0]) / 6)} ${f1(p2[1] - (p3[1] - p1[1]) / 6)} ${f1(p2[0])} ${f1(p2[1])}`;
    }
    return `${d} Z`;
  }

  function abierta(pts) {
    let d = `M${f1(pts[0][0])} ${f1(pts[0][1])}`;
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
      d += ` C${f1(p1[0] + (p2[0] - p0[0]) / 6)} ${f1(p1[1] + (p2[1] - p0[1]) / 6)} ${f1(p2[0] - (p3[0] - p1[0]) / 6)} ${f1(p2[1] - (p3[1] - p1[1]) / 6)} ${f1(p2[0])} ${f1(p2[1])}`;
    }
    return d;
  }

  // Sin medida: un punto en el centro, para que la forma crezca desde ahí al aparecer.
  const forma = (medida, escala) =>
    cerrada(medida ? medida.map((v, i) => punto(i, R * (0.12 + 0.88 * Math.min(1, v / (escala[i] || 1))))) : EJES.map((_, i) => punto(i, 0.1)));
  const medir = (v) => (estado.escala === "perfil" ? perfil(v) : v.slice(0, N));
  const escribirMedida = (n) => (estado.escala === "perfil" ? pct(n) : String(Math.round(n)));

  const ANILLOS = [0.25, 0.5, 0.75, 1].map((f) => { const r = R * (0.12 + 0.88 * f); return `M${f1(r)} 0 A${f1(r)} ${f1(r)} 0 1 1 ${f1(-r)} 0 A${f1(r)} ${f1(r)} 0 1 1 ${f1(r)} 0`; }).join(" ");
  const RAYOS = EJES.map((_, i) => `M0 0 L${punto(i, R).map(f1).join(" ")}`).join(" ");

  function trazar(el, d) {
    el.setAttribute("d", d);
    el.style.d = `path("${d}")`;
  }

  function svg(nombre, atributos = {}) {
    const el = document.createElementNS(NS, nombre);
    for (const [clave, v] of Object.entries(atributos)) el.setAttribute(clave, v);
    return el;
  }

  // Un radial: anillos, rayos, el eje del indicador resaltado, una referencia punteada y hasta tres formas. Las etiquetas van en
  // HTML sobre el dibujo (no se achican con él).
  function crearRadial() {
    const raiz = document.createElement("div");
    raiz.className = "radial-b";
    const dibujo = svg("svg", { viewBox: "-280 -230 560 460", class: "radial-b__dibujo", role: "img" });
    dibujo.append(svg("path", { d: ANILLOS, class: "radial-b__anillos" }), svg("path", { d: RAYOS, class: "radial-b__rayos" }));
    const eje = svg("path", { class: "radial-b__eje", d: "M0 0 L0 0" });
    const referencia = svg("path", { class: "radial-b__referencia" });
    const series = [0, 1, 2].map(() => svg("path", { class: "radial-b__serie" }));
    for (const el of [referencia, ...series]) trazar(el, forma(null));
    dibujo.append(eje, referencia, ...series);
    const etiquetas = EJES.map((x, i) => {
      const [px, py] = punto(i, R + 16);
      const span = document.createElement("span");
      span.className = "radial-b__etiqueta";
      span.textContent = x.corto;
      span.style.left = `${((px + 280) / 5.6).toFixed(2)}%`;
      span.style.top = `${((py + 230) / 4.6).toFixed(2)}%`;
      span.style.transform = Math.abs(px) < 6 ? "translate(-50%, -50%)" : px > 0 ? "translate(0, -50%)" : "translate(-100%, -50%)";
      return span;
    });
    raiz.append(dibujo, ...etiquetas);
    return { raiz, dibujo, eje, referencia, series, etiquetas };
  }

  // series: [{ medida, color }] (hasta 3; null = oculta). referencia: una medida o null.
  function pintarRadial(radial, series, referencia, escala, descripcion) {
    radial.dibujo.setAttribute("aria-label", descripcion);
    const ki = k();
    radial.eje.setAttribute("d", ki < 0 ? "M0 0 L0 0" : `M0 0 L${punto(ki, R).map(f1).join(" ")}`);
    radial.eje.classList.toggle("radial-b__eje--visible", ki >= 0);
    radial.etiquetas.forEach((s, i) => s.classList.toggle("radial-b__etiqueta--elegida", i === ki));
    trazar(radial.referencia, forma(referencia, escala));
    radial.referencia.classList.toggle("radial-b__serie--oculta", !referencia);
    radial.series.forEach((el, j) => {
      const s = series[j];
      trazar(el, forma(s?.medida ?? null, escala));
      if (s) el.style.setProperty("--color", s.color);
      el.classList.toggle("radial-b__serie--oculta", !s?.medida);
    });
  }

  const escalaDe = (medidas) => EJES.map((_, i) => Math.max(1e-6, ...medidas.filter(Boolean).map((m) => m[i])));
  const describir = (nombre, v) => `${nombre}: ${EJES.map((x, i) => `${x.corto} ${escribirMedida(medir(v)[i])}`).join(", ")}`;
  const masFrecuente = (dif, max = true) => EJES[dif.indexOf(max ? Math.max(...dif) : Math.min(...dif))].nombre.toLowerCase();

  // ---------- Panel de noche: tarjetas (uno por bloque o por zona) o un radial grande ----------
  panel.innerHTML = `
    <div class="tarjetas-b" data-tarjetas></div>
    <p class="noche-b__nota" data-mas-zonas hidden></p>
    <div class="grande-b" data-grande hidden>
      <div class="grande-b__radial" data-radial-grande></div>
      <div class="grande-b__texto">
        <span class="noche-b__nota" data-nota-grande></span>
        <div class="grande-b__leyenda" data-leyenda-grande></div>
        <p class="grande-b__lectura" data-lectura-grande></p>
      </div>
    </div>
    <p class="noche-b__nota noche-b__nota--pie" data-nota-escala></p>`;
  const contenedorTarjetas = panel.querySelector("[data-tarjetas]");
  const tarjetas = new Map();
  const grande = crearRadial();
  panel.querySelector("[data-radial-grande]").append(grande.raiz);

  function tarjeta(clave) {
    if (tarjetas.has(clave)) return tarjetas.get(clave);
    const raiz = document.createElement("article");
    raiz.className = "tarjeta-radial-b";
    raiz.innerHTML = `<header class="tarjeta-radial-b__cabecera"><span class="muestra-b"></span><h4></h4><span class="tarjeta-radial-b__cifra"></span></header>
      <p class="tarjeta-radial-b__lectura"></p><button type="button" class="boton-noche-b"></button>`;
    const radial = crearRadial();
    raiz.querySelector("header").after(radial.raiz);
    const t = { raiz, radial, nueva: true };
    tarjetas.set(clave, t);
    return t;
  }

  function dibujarPerfil() {
    const b = elegido();
    const z = conZonas();
    const verTarjetas = estado.vista === "bloques" && (!b || z);
    $("[data-titulo-perfil]").textContent = estado.vista === "comparar"
      ? (!b ? "Comparar los bloques" : z ? `Las zonas elegidas frente al resto del bloque ${b.nombre}` : `El bloque ${b.nombre} frente al resto del distrito`)
      : (!b ? "El perfil de cada bloque" : z ? `Las zonas elegidas del bloque ${b.nombre}, una por una` : `El perfil del bloque ${b.nombre}`);
    pintarSegmentado($('[data-control="vista"]'), "vista", [["bloques", b ? "Uno por zona" : "Uno por bloque"], ["comparar", "Comparar en un radial"]]);
    pintarSegmentado($('[data-control="escala"]'), "escala", [["perfil", "Perfil (%)"], ["cantidad", "Cantidad"]]);
    $("[data-nota-escala]").textContent = (estado.escala === "perfil"
      ? "Perfil: qué parte de sus casos es cada tipo, para comparar formas aunque un lugar tenga muchos más casos."
      : "Cantidad: el número de casos de cada tipo.")
      + (b && z
        ? ` Cada eje usa la escala de todas las zonas del bloque, así una zona se ve igual sola o junto a otras.${estado.escala === "perfil" ? " La línea punteada es el perfil de todo el bloque." : ""}`
        : " Los radiales de un mismo panel comparten la escala de cada eje.");
    contenedorTarjetas.hidden = !verTarjetas;
    panel.querySelector("[data-grande]").hidden = verTarjetas;
    const masZonas = panel.querySelector("[data-mas-zonas]");
    masZonas.hidden = !(verTarjetas && z && estado.zonas.length > MAX_ZONAS_RADIAL);
    if (!masZonas.hidden)
      masZonas.textContent = `Se muestran las primeras ${MAX_ZONAS_RADIAL} zonas elegidas; las otras ${estado.zonas.length - MAX_ZONAS_RADIAL} cuentan en el reparto y en la evolución.`;
    if (verTarjetas) dibujarTarjetas(b); else dibujarGrande(b, z);
  }

  function dibujarTarjetas(b) {
    let items;
    if (!b) {
      const promedio = EJES.map((_, i) => bloques.reduce((s, x) => s + perfil(x.casos)[i], 0) / bloques.length);
      items = bloques.map((x) => ({
        clave: `b:${x.clave}`, nombre: `Bloque ${x.nombre}`, casos: x.casos, color: x.color.oscuro,
        lectura: `Pesa más que en los otros bloques: ${masFrecuente(perfil(x.casos).map((p, i) => p - promedio[i]))}.`,
        boton: "Ver sus zonas →", accion: "bloque", valor: x.clave,
      }));
    } else {
      const base = perfil(b.casos);
      items = estado.zonas.slice(0, MAX_ZONAS_RADIAL).map((nombre) => {
        const zona = zonas.get(nombre);
        return {
          clave: `z:${nombre}`, nombre: `Zona ${nombre}`, casos: zona.casos, color: b.color.oscuro,
          lectura: `Frente a todo el bloque, pesa más ${masFrecuente(perfil(zona.casos).map((p, i) => p - base[i]))}.`,
          boton: "Quitar del filtro", accion: "zona", valor: nombre,
        };
      });
    }
    const medidas = items.map((x) => medir(x.casos));
    // Escala de cada eje. Con zonas, la de todas las zonas del bloque, no solo la de las elegidas: con una zona sola su máximo sería
    // ella misma y todos los ejes llegarían al borde (un círculo), y la forma cambiaría según qué otras zonas se elijan. Las zonas
    // con pocos casos no fijan la escala (un 100 % de cinco casos achicaría a todas las demás); si la pasan, llegan al borde.
    const deEscala = b ? b.zonas.filter((z) => z.casos[TOTAL] >= ZONA_PARA_ESCALA) : [];
    const escala = b ? escalaDe((deEscala.length ? deEscala : b.zonas).map((z) => medir(z.casos))) : escalaDe(medidas);
    // En perfil, cada zona lleva de referencia el perfil de todo su bloque (punteado).
    const referencia = b && estado.escala === "perfil" ? medir(b.casos) : null;
    const vigentes = new Set(items.map((x) => x.clave));
    for (const [clave, t] of tarjetas) if (!vigentes.has(clave)) { t.raiz.remove(); tarjetas.delete(clave); }
    const nuevas = [];
    items.forEach((x, j) => {
      const t = tarjeta(x.clave);
      const raiz = t.raiz;
      raiz.querySelector(".muestra-b").style.background = x.color;
      raiz.querySelector("h4").textContent = x.nombre;
      raiz.querySelector(".tarjeta-radial-b__cifra").textContent = `${valor(x.casos)} · ${k() < 0 ? "todos" : EJES[k()].corto.toLowerCase()}`;
      raiz.querySelector(".tarjeta-radial-b__lectura").textContent = x.lectura;
      const boton = raiz.querySelector("button");
      boton.textContent = x.boton;
      boton.dataset.accion = x.accion;
      boton.dataset.valor = x.valor;
      contenedorTarjetas.append(raiz); // en el orden de la lista
      const pintar = () => pintarRadial(t.radial, [{ medida: medidas[j], color: x.color }], referencia, escala, describir(x.nombre, x.casos));
      if (t.nueva) { nuevas.push(pintar); t.nueva = false; } else pintar();
    });
    // Las tarjetas nuevas parten del centro: se lee el diseño una vez para que la transición tenga desde dónde salir.
    if (nuevas.length) { void contenedorTarjetas.offsetHeight; nuevas.forEach((f) => f()); }
  }

  function dibujarGrande(b, z) {
    let series = [], referencia = null, nombreReferencia = "", conmutables = false;
    if (estado.vista === "comparar" && !b) {
      series = bloques.map((x) => ({ nombre: `Bloque ${x.nombre}`, color: x.color.oscuro, v: estado.comparar.includes(x.clave) ? x.casos : null, clave: x.clave }));
      conmutables = true;
    } else if (estado.vista === "comparar") {
      const sel = seleccion();
      const resto = z ? restar(b.casos, sel) : restar(distrito, b.casos);
      series = [{ nombre: z ? "Zonas elegidas" : `Bloque ${b.nombre}`, color: b.color.oscuro, v: sel }, { nombre: z ? "Resto del bloque" : "Resto del distrito", color: GRIS_NOCHE, v: resto }];
    } else {
      series = [{ nombre: `Bloque ${b.nombre}`, color: b.color.oscuro, v: b.casos }];
      if (estado.escala === "perfil") { referencia = distrito; nombreReferencia = "Todo el distrito"; }
    }
    const medidas = series.map((s) => (s.v ? medir(s.v) : null));
    const medidaReferencia = referencia ? medir(referencia) : null;
    // Con una sola forma y sin referencia (un bloque en cantidad, o un solo bloque marcado al comparar), su máximo sería ella misma y
    // saldría un círculo: la escala es la de los tres bloques.
    const unaSola = medidas.filter(Boolean).length === 1 && !medidaReferencia;
    const escala = unaSola ? escalaDe(bloques.map((x) => medir(x.casos))) : escalaDe([...medidas, medidaReferencia]);
    pintarRadial(grande, series.map((s, j) => (s.v ? { medida: medidas[j], color: s.color } : null)), medidaReferencia, escala,
      series.filter((s) => s.v).map((s) => describir(s.nombre, s.v)).join("; "));
    $("[data-nota-grande]").textContent = `${estado.escala === "perfil" ? "En % de sus casos" : "En número de casos"} · cada eje con su escala`;
    let leyenda = series.map((s) => {
      const activo = !!s.v;
      const muestra = `<span class="muestra-b" style="background:${s.color};opacity:${activo ? 1 : 0.35}"></span>`;
      return conmutables
        ? `<button type="button" class="leyenda-noche-b" data-accion="comparar" data-valor="${s.clave}" aria-pressed="${activo}">${muestra}${e(s.nombre)}</button>`
        : `<span class="leyenda-noche-b">${muestra}${e(s.nombre)}</span>`;
    }).join("");
    if (referencia) leyenda += `<span class="leyenda-noche-b"><span class="muestra-b muestra-b--punteada"></span>${nombreReferencia} (punteado)</span>`;
    $("[data-leyenda-grande]").innerHTML = leyenda;
    let lectura = "Toca los nombres para sumar o quitar un bloque de la comparación.";
    const base = referencia ?? series[1]?.v;
    if (series[0].v && base && !conmutables) {
      const dif = perfil(series[0].v).map((p, i) => p - perfil(base)[i]);
      const contra = (nombreReferencia || series[1].nombre).toLowerCase();
      lectura = `${series[0].nombre}: frente ${contra.startsWith("resto") ? "al" : "a"} ${contra}, pesa más ${masFrecuente(dif)} y menos ${masFrecuente(dif, false)}.`;
    }
    if (b && !z && estado.vista === "bloques") lectura += " Elige zonas arriba para ver un radial por zona.";
    $("[data-lectura-grande]").textContent = lectura;
  }

  function pintarSegmentado(contenedor, accion, opciones) {
    contenedor.innerHTML = opciones.map(([v, texto]) =>
      `<button type="button" class="segmentado__opcion" data-accion="${accion}" data-valor="${v}" aria-pressed="${estado[accion] === v}">${e(texto)}</button>`).join("");
  }

  // ---------- Filtros y titular ----------
  const chip = (activo, texto, accion, valorChip, { color = null, extra = "" } = {}) =>
    `<button type="button" class="chip-b" data-accion="${accion}" data-valor="${e(valorChip)}" aria-pressed="${activo}"${color ? ` style="--acento:${color}"` : ""}>${color ? '<span class="chip-b__punto" aria-hidden="true"></span>' : ""}${e(texto)}${extra}</button>`;

  function dibujarFiltros() {
    filaBloque.innerHTML = chip(estado.bloque === "todos", "Todos", "bloque", "todos")
      + bloques.map((b) => chip(estado.bloque === b.clave, b.nombre, "bloque", b.clave, { color: b.color.claro })).join("");
    const b = elegido();
    contenedorZonas.hidden = !b;
    if (b) {
      const reunidas = b.zonasReunidas > 0 ? `<span class="filtros-b__nota">${b.zonasReunidas} ${b.zonasReunidas === 1 ? "zona" : "zonas"} con menos de ${D.minimoZona} casos suman en el bloque, no solas</span>` : "";
      filaZonas.innerHTML = `<button type="button" class="chip-b" data-accion="todas-las-zonas" aria-pressed="${estado.zonas.length === 0}">Todas</button>`
        + b.zonas.map((z) => chip(estado.zonas.includes(z.nombre), z.nombre, "zona", z.nombre,
          { color: b.color.claro, extra: `<span class="chip-b__cifra">${valor(z.casos)}</span>` })).join("")
        + reunidas;
    }
    filaIndicador.innerHTML = chip(estado.indicador === "todos", "Todos los casos", "indicador", "todos")
      + EJES.map((x) => chip(estado.indicador === x.clave, x.nombre, "indicador", x.clave)).join("");
  }

  function dibujarTitular() {
    const b = elegido();
    let html;
    if (!b) {
      const mayor = bloques.reduce((x, y) => (valor(y.casos) > valor(x.casos) ? y : x));
      html = `El bloque ${e(mayor.nombre)} reúne <em>${casosDe(valor(mayor.casos))}</em> en ${e(D.periodo)}.`;
    } else if (!conZonas()) {
      html = `El bloque ${e(b.nombre)} registró <em>${casosDe(valor(b.casos))}</em> en ${e(D.periodo)}.`;
    } else {
      const n = estado.zonas.length;
      html = `${n === 1 ? `La zona ${e(estado.zonas[0])} suma` : `Las ${n} zonas elegidas suman`} <em>${casosDe(valor(seleccion()))}</em>, de los ${valor(b.casos)} del bloque ${e(b.nombre)}.`;
    }
    if (titular.innerHTML !== html) titular.innerHTML = html;
  }

  // ---------- Reparto (semicírculo) ----------
  const grafico = $("[data-grafico-reparto]");
  grafico.innerHTML = `<svg viewBox="0 0 560 290" class="reparto-b__arco" role="img"></svg>
    <div class="reparto-b__centro"><b data-total-reparto></b><span data-sub-reparto></span></div>`;
  const arcoSvg = grafico.querySelector("svg");
  const segmentos = [0, 1, 2, 3, 4].map(() => { const p = svg("path", { class: "reparto-b__segmento" }); arcoSvg.append(p); return p; });

  function arco(a0, a1) {
    const cx = 280, cy = 270, ri = 150, re = 238;
    const p = (r, a) => `${f1(cx + Math.cos(a) * r)} ${f1(cy + Math.sin(a) * r)}`;
    return `M${p(re, a0)} A${re} ${re} 0 0 1 ${p(re, a1)} L${p(ri, a1)} A${ri} ${ri} 0 0 0 ${p(ri, a0)} Z`;
  }

  function dibujarReparto() {
    const b = elegido();
    const z = conZonas();
    let partes;
    if (!z) {
      partes = bloques.map((x) => ({
        nombre: `Bloque ${x.nombre}`, valor: valor(x.casos), activo: !b || x.clave === b.clave, resaltar: !!b && x.clave === b.clave, rgb: x.color.rgb,
        color: !b || x.clave === b.clave ? x.color.claro : `rgb(${x.color.rgb} / 0.28)`, accion: "bloque", valorAccion: x.clave,
      }));
    } else {
      const lista = estado.zonas.slice(0, MAX_ZONAS_REPARTO);
      partes = lista.map((nombre, i) => ({
        nombre: `Zona ${nombre}`, valor: valor(zonas.get(nombre).casos), activo: true, resaltar: false, rgb: b.color.rgb,
        color: `rgb(${b.color.rgb} / ${TONOS_ZONA[i]})`, accion: "zona", valorAccion: nombre,
      }));
      const resto = valor(b.casos) - partes.reduce((s, p) => s + p.valor, 0);
      partes.push({
        nombre: estado.zonas.length > MAX_ZONAS_REPARTO ? "Otras zonas elegidas y resto del bloque" : "Resto del bloque", valor: Math.max(0, resto),
        activo: true, resaltar: false, rgb: "116 135 162", color: "rgb(116 135 162 / 0.32)", accion: "todas-las-zonas", valorAccion: "",
      });
    }
    const suma = partes.reduce((s, p) => s + p.valor, 0);
    let a = Math.PI;
    segmentos.forEach((el, j) => {
      const p = partes[j];
      const a0 = a, a1 = p && suma > 0 ? a + (Math.PI * p.valor) / suma : a;
      a = a1;
      const hueco = p && p.valor > 0 ? 0.01 : 0;
      trazar(el, arco(a0 + hueco, Math.max(a0 + hueco, a1 - hueco)));
      el.style.fill = p ? p.color : "transparent";
    });
    arcoSvg.setAttribute("aria-label", partes.map((p) => `${p.nombre}: ${p.valor}`).join(", "));
    $("[data-titulo-reparto]").textContent = z ? `Cómo se reparte dentro del bloque ${b.nombre}` : "Cómo se reparte entre los bloques";
    $("[data-ayuda-reparto]").textContent = z
      ? `Qué parte de ${nombreIndicador().toLowerCase()} del bloque ocurre en cada zona elegida. Toca una para quitarla del filtro.`
      : `Qué parte de ${nombreIndicador().toLowerCase()} ocurre en cada bloque. Toca uno para elegirlo.`;
    $("[data-total-reparto]").textContent = suma;
    $("[data-sub-reparto]").textContent = `${nombreIndicador()} ${z ? "en el bloque" : "en el distrito"}`;
    $("[data-lista-reparto]").innerHTML = partes.map((p) => `<li><button type="button" class="parte-b${p.resaltar ? " parte-b--elegida" : ""}${p.activo ? "" : " parte-b--tenue"}" data-accion="${p.accion}" data-valor="${e(p.valorAccion)}" style="--rgb:${p.rgb}">
        <span class="muestra-b" style="background:${p.color}"></span><span class="parte-b__nombre">${e(p.nombre)}</span><b>${p.valor}</b><span class="parte-b__pct">${suma ? Math.round((100 * p.valor) / suma) : 0} %</span></button></li>`).join("");
  }

  // ---------- Fichas ----------
  function dibujarFichas() {
    const b = elegido();
    const ki = k();
    const masComun = (v) => EJES[v.slice(0, N).indexOf(Math.max(...v.slice(0, N)))].nombre.toLowerCase();
    let html;
    if (!conZonas()) {
      $("[data-titulo-fichas]").textContent = "Los tres bloques";
      $("[data-ayuda-fichas]").textContent = "Cada bloque agrupa las zonas que atiende una unidad policial del distrito. Toca uno para elegirlo.";
      html = bloques.map((x) => {
        const foco = !!b && x.clave === b.clave;
        return `<button type="button" class="ficha-b${foco ? " ficha-b--elegida" : ""}${b && !foco ? " ficha-b--tenue" : ""}" data-accion="bloque" data-valor="${x.clave}" aria-pressed="${foco}" style="--acento:${x.color.claro};--rgb:${x.color.rgb}">
          <span class="ficha-b__cabecera"><span class="muestra-b" style="background:${x.color.claro}"></span><b>Bloque ${e(x.nombre)}</b><span>${x.zonasDelBloque} zonas</span></span>
          <span class="ficha-b__linea">${e(x.unidad)} · ${e(x.direccion)}</span>
          <span class="ficha-b__datos"><span>${x.casos[TOTAL]} casos en ${e(D.periodo)}</span>${ki < 0 ? "" : `<span>${x.casos[ki]} de ${e(EJES[ki].corto.toLowerCase())}</span>`}<span class="ficha-b__suave">lo más frecuente: ${e(masComun(x.casos))}</span></span>
        </button>`;
      }).join("");
    } else {
      $("[data-titulo-fichas]").textContent = "Las zonas elegidas";
      $("[data-ayuda-fichas]").textContent = `Celdas de unos 550 m del bloque ${b.nombre}. Toca una para quitarla del filtro.`;
      const base = perfil(b.casos);
      html = estado.zonas.slice(0, MAX_ZONAS_RADIAL).map((nombre) => {
        const zona = zonas.get(nombre);
        return `<button type="button" class="ficha-b" data-accion="zona" data-valor="${e(nombre)}" style="--acento:${b.color.claro};--rgb:${b.color.rgb}">
          <span class="ficha-b__cabecera"><span class="muestra-b" style="background:${b.color.claro}"></span><b>Zona ${e(nombre)}</b><span>bloque ${e(b.nombre)}</span></span>
          <span class="ficha-b__linea">Lo que más la distingue del bloque: ${e(masFrecuente(perfil(zona.casos).map((p, i) => p - base[i])))}</span>
          <span class="ficha-b__datos"><span>${zona.casos[TOTAL]} casos en ${e(D.periodo)}</span>${ki < 0 ? "" : `<span>${zona.casos[ki]} de ${e(EJES[ki].corto.toLowerCase())}</span>`}<span class="ficha-b__suave">lo más frecuente: ${e(masComun(zona.casos))}</span></span>
        </button>`;
      }).join("");
    }
    $("[data-fichas]").innerHTML = html;
  }

  // ---------- Evolución de 12 meses ----------
  const ALTO = 280;
  const contenedorEvolucion = $("[data-grafico-evolucion]");
  contenedorEvolucion.innerHTML = `
    <div class="evolucion-b__plano">
      <div data-temporadas></div><div data-marcas></div>
      <svg viewBox="0 0 1000 ${ALTO}" preserveAspectRatio="none" class="evolucion-b__dibujo" aria-hidden="true">
        <defs>${bloques.map((b) => `<linearGradient id="area-${b.clave}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${b.color.claro}" stop-opacity="0.22"/><stop offset="1" stop-color="${b.color.claro}" stop-opacity="0"/></linearGradient>`).join("")}</defs>
      </svg>
      <div data-finales></div>
      <div class="evolucion-b__guia" data-guia hidden></div><div data-puntos-guia></div>
      <div class="evolucion-b__globo" data-globo hidden></div>
      <div class="evolucion-b__columnas">${D.meses.map((_, i) => `<span data-mes="${i}"></span>`).join("")}</div>
    </div>
    <div class="evolucion-b__meses">${D.meses.map((m, i) => `<span>${m.corto}${i === 0 || m.mes === 1 ? `<small>${m.anio}</small>` : ""}</span>`).join("")}</div>
    <table class="visualmente-oculto" data-tabla-evolucion></table>`;
  const dibujoEvolucion = contenedorEvolucion.querySelector("svg");
  const areas = [0, 1, 2].map(() => { const p = svg("path", { class: "evolucion-b__area" }); dibujoEvolucion.append(p); return p; });
  const lineas = [0, 1, 2].map(() => { const p = svg("path", { class: "evolucion-b__linea", "vector-effect": "non-scaling-stroke" }); dibujoEvolucion.append(p); return p; });
  const xMes = (i) => (i / (D.meses.length - 1)) * 1000;
  let series = [];
  let tope = 1;
  const y = (v) => ALTO - 10 - (v / tope) * (ALTO - 30);

  // Temporadas (meses seguidos con el mismo nombre forman una franja).
  const TEMPORADA = { 12: "Fin de año", 1: "Verano", 2: "Verano", 3: "Verano", 7: "Fiestas Patrias", 8: "Aniversario de Arequipa" };
  const franjas = [];
  D.meses.forEach((m, i) => {
    const nombre = TEMPORADA[m.mes];
    if (!nombre) return;
    if (franjas.length && franjas.at(-1).nombre === nombre && franjas.at(-1).hasta === i - 1) franjas.at(-1).hasta = i;
    else franjas.push({ nombre, desde: i, hasta: i });
  });
  const ultimo = D.meses.length - 1;
  contenedorEvolucion.querySelector("[data-temporadas]").innerHTML = franjas.map((t) => {
    const izquierda = Math.max(0, ((t.desde - 0.5) / ultimo) * 100), derecha = Math.min(100, ((t.hasta + 0.5) / ultimo) * 100);
    return `<div class="evolucion-b__temporada" style="left:${izquierda.toFixed(2)}%;width:${(derecha - izquierda).toFixed(2)}%"><span>${t.nombre}</span></div>`;
  }).join("");

  function dibujarEvolucion() {
    const b = elegido();
    const z = conZonas();
    const linea = (meses) => meses.map(valor);
    series = z
      ? [{ nombre: "Zonas elegidas", corto: "Zonas", color: b.color.claro, area: `url(#area-${b.clave})`, datos: linea(mesesDeZonas(estado.zonas)), punteada: false },
         { nombre: `Bloque ${b.nombre} entero`, corto: "Bloque", color: GRIS, area: null, datos: linea(b.meses), punteada: true }]
      : bloques.map((x) => {
        const foco = !b || x.clave === b.clave;
        return { nombre: `Bloque ${x.nombre}`, corto: CORTOS_BLOQUE[x.clave] ?? x.nombre, color: foco ? x.color.claro : "#A9B6C8", area: foco ? `url(#area-${x.clave})` : null, datos: linea(x.meses), punteada: false };
      });
    const maximo = Math.max(1, ...series.flatMap((s) => s.datos));
    const paso = maximo > 400 ? 100 : maximo > 100 ? 50 : maximo > 40 ? 10 : maximo > 10 ? 5 : 1;
    tope = Math.ceil((maximo * 1.12) / paso) * paso;
    $("[data-titulo-evolucion]").textContent = `Evolución de ${nombreIndicador().toLowerCase()} en los últimos 12 meses completos`;
    $("[data-leyenda-evolucion]").innerHTML = series.map((s) =>
      `<li><span class="${s.punteada ? "muestra-b muestra-b--linea-punteada" : "muestra-b muestra-b--linea"}" style="--color:${s.color}"></span>${e(s.nombre)}</li>`).join("");
    contenedorEvolucion.querySelector("[data-marcas]").innerHTML = [0, 0.25, 0.5, 0.75, 1].map((f) =>
      `<div class="evolucion-b__marca${f === 0 ? " evolucion-b__marca--base" : ""}" style="top:${((y(tope * f) / ALTO) * 100).toFixed(2)}%"><span>${Math.round(tope * f)}</span></div>`).join("");
    [0, 1, 2].forEach((j) => {
      const s = series[j];
      const pts = s ? s.datos.map((v, i) => [xMes(i), y(v)]) : D.meses.map((_, i) => [xMes(i), ALTO - 10]);
      const d = abierta(pts);
      trazar(lineas[j], d);
      trazar(areas[j], `${d} L1000 ${ALTO - 10} L0 ${ALTO - 10} Z`);
      lineas[j].style.stroke = s ? s.color : GRIS;
      lineas[j].classList.toggle("evolucion-b__linea--punteada", !!s?.punteada);
      lineas[j].classList.toggle("evolucion-b__linea--oculta", !s);
      areas[j].style.fill = s?.area ?? "none";
      areas[j].classList.toggle("evolucion-b__linea--oculta", !s?.area);
    });
    // Rótulos al final de cada línea, separados si quedan encima uno de otro (16 px como mínimo).
    const finales = series.map((s) => ({ s, top: y(s.datos.at(-1)) })).sort((a, c) => a.top - c.top);
    for (let i = 1; i < finales.length; i++) finales[i].top = Math.max(finales[i].top, finales[i - 1].top + 16);
    contenedorEvolucion.querySelector("[data-finales]").innerHTML = finales.map(({ s, top }) =>
      `<span class="evolucion-b__final" style="top:${((top / ALTO) * 100).toFixed(2)}%;--color:${s.color}" title="${e(s.nombre)}">${e(s.corto)} · ${s.datos.at(-1)}</span>`).join("");
    const lectura = z ? series[0].datos : b ? linea(b.meses) : D.meses.map((_, i) => series.reduce((suma, s) => suma + s.datos[i], 0));
    const pico = lectura.indexOf(Math.max(...lectura)), valle = lectura.indexOf(Math.min(...lectura));
    $("[data-lectura-evolucion]").textContent = `${z ? "En las zonas elegidas, el" : b ? `En el bloque ${b.nombre}, el` : "En el distrito, el"} mes más alto es ${D.meses[pico].largo} y el más bajo, ${D.meses[valle].largo}. Pasa el cursor sobre un mes para ver los valores.`;
    $("[data-tabla-evolucion]").innerHTML = `<caption>${e($("[data-titulo-evolucion]").textContent)}</caption><thead><tr><th scope="col">Mes</th>${series.map((s) => `<th scope="col">${e(s.nombre)}</th>`).join("")}</tr></thead><tbody>${D.meses.map((m, i) => `<tr><th scope="row">${m.largo}</th>${series.map((s) => `<td>${s.datos[i]}</td>`).join("")}</tr>`).join("")}</tbody>`;
    dibujarGuia();
  }

  function dibujarGuia() {
    const guia = $("[data-guia]"), globo = $("[data-globo]"), puntos = $("[data-puntos-guia]");
    const i = estado.mes;
    guia.hidden = globo.hidden = i === null;
    if (i === null) { puntos.innerHTML = ""; return; }
    const izquierda = (xMes(i) / 1000) * 100;
    guia.style.left = `${izquierda}%`;
    puntos.innerHTML = series.map((s) => `<span class="evolucion-b__punto" style="left:${izquierda}%;top:${((y(s.datos[i]) / ALTO) * 100).toFixed(2)}%;--color:${s.color}"></span>`).join("");
    globo.innerHTML = `<b>${D.meses[i].largo}</b>${series.map((s) => `<span><span class="muestra-b muestra-b--redonda" style="background:${s.color}"></span>${e(s.nombre)}<b>${s.datos[i]}</b></span>`).join("")}`;
    globo.style.left = i > ultimo / 2 ? "auto" : `calc(${izquierda}% + 12px)`;
    globo.style.right = i > ultimo / 2 ? `calc(${100 - izquierda}% + 12px)` : "auto";
  }

  const columnas = contenedorEvolucion.querySelector(".evolucion-b__columnas");
  columnas.addEventListener("pointerover", (ev) => {
    const mes = ev.target.closest("[data-mes]");
    if (mes) { estado.mes = Number(mes.dataset.mes); dibujarGuia(); }
  });
  columnas.addEventListener("pointerleave", () => { estado.mes = null; dibujarGuia(); });

  // ---------- Dirección: la selección se guarda y viaja con el periodo ----------
  function parametros() {
    const p = {};
    if (estado.bloque !== "todos") p.bloque = estado.bloque;
    if (conZonas()) p.zonas = estado.zonas.join(",");
    if (estado.indicador !== "robo") p.indicador = estado.indicador;
    if (estado.vista !== "bloques") p.vista = estado.vista;
    if (estado.escala !== "perfil") p.escala = estado.escala;
    return p;
  }

  function guardar() {
    const p = parametros();
    const aplicar = (url) => {
      for (const clave of ["bloque", "zonas", "indicador", "vista", "escala"]) url.searchParams.delete(clave);
      for (const [clave, v] of Object.entries(p)) url.searchParams.set(clave, v);
      return url;
    };
    history.replaceState(null, "", aplicar(new URL(location.href)));
    for (const enlace of document.querySelectorAll("[data-filtros] a.segmentado__opcion")) enlace.href = aplicar(new URL(enlace.href)).toString();
    const formulario = document.querySelector("[data-filtros] .rango__campos");
    if (formulario) {
      formulario.querySelectorAll("[data-seleccion-bloques]").forEach((n) => n.remove());
      for (const [clave, v] of Object.entries(p)) {
        const oculto = Object.assign(document.createElement("input"), { type: "hidden", name: clave, value: v });
        oculto.dataset.seleccionBloques = "";
        formulario.append(oculto);
      }
    }
  }

  function dibujar() {
    dibujarFiltros();
    dibujarTitular();
    dibujarPerfil();
    dibujarReparto();
    dibujarFichas();
    dibujarEvolucion();
  }

  dibujar();
  guardar();
})();
