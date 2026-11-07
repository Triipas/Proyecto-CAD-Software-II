// Sonidos del panel (24/10/2026). Activados por defecto; el operador los silencia con el botón de la barra superior y la
// preferencia queda en este navegador. Cada aviso sonoro tiene siempre su aviso visual.
(() => {
  const CLAVE = "cad.avisosSonoros";
  // Los de 07/11/2026 son mp3; los anteriores, wav.
  const EXTENSIONES = {
    "sos-nuevo": "wav", "reporte-admitido": "wav", "caso-asignado": "wav", "aviso-acompanamiento": "wav", "error": "wav",
    "reporte-por-revisar": "mp3", "sereno-en-camino": "mp3", "atencion-iniciada": "mp3", "sereno-disponible-con-pendientes": "mp3",
    "conexion-perdida": "mp3", "conexion-recuperada": "mp3", "operacion-confirmada": "mp3",
  };
  const NOMBRES = Object.keys(EXTENSIONES);
  const boton = document.querySelector("[data-alternar-sonido]");
  const audios = new Map();
  let activos = leer();

  function leer() {
    try { return localStorage.getItem(CLAVE) !== "0"; } catch { return true; }
  }
  function guardar(valor) {
    try { valor ? localStorage.removeItem(CLAVE) : localStorage.setItem(CLAVE, "0"); } catch { /* sin almacenamiento: dura la página */ }
  }

  function audio(nombre) {
    let a = audios.get(nombre);
    if (!a) {
      a = new Audio(`/sonidos/${nombre}.${EXTENSIONES[nombre] ?? "wav"}`);
      a.preload = "auto";
      audios.set(nombre, a);
    }
    return a;
  }

  // Dentro del panel del caso de la central (un marco) el sonido lo toca la central, no el marco: al asignar, el marco se
  // recarga y la central lo cierra enseguida, y un sonido que sonara en él se cortaría (07/11/2026).
  const enMarco = window.parent !== window;
  function anfitrion() {
    try { return enMarco ? window.parent.CadSonidos ?? null : null; } catch { return null; }
  }

  // Los sonidos suenan de uno en uno, sin pisarse: si ya suena uno, el nuevo espera su turno (sin repetir uno que ya espera).
  // Un SOS nuevo no espera: corta lo que suena y pasa primero.
  const espera = [];
  let sonando = null;
  function siguiente() {
    sonando = null;
    const nombre = espera.shift();
    if (nombre) tocar(nombre);
  }
  function tocar(nombre) {
    const a = audio(nombre);
    a.currentTime = 0;
    a.onended = a.onerror = () => { if (sonando === a) siguiente(); };
    sonando = a;
    a.play().catch((error) => {
      if (error.name === "NotAllowedError") { boton?.setAttribute("data-bloqueado", ""); espera.length = 0; }
      if (sonando === a) siguiente();
    });
  }

  // Devuelve si se intentó reproducir (avisos activados). Los navegadores bloquean el sonido hasta que haya una acción del
  // usuario en la página (Plan de Arquitectura, SP-E): entonces el botón lo señala y el primer clic lo habilita.
  function reproducir(nombre) {
    if (!activos) return false;
    const central = anfitrion();
    if (central) return central.reproducir(nombre);
    if (nombre === "sos-nuevo") {
      espera.length = 0;
      sonando?.pause();
      tocar(nombre);
    } else if (!sonando) tocar(nombre);
    else if (audios.get(nombre) !== sonando && !espera.includes(nombre) && espera.length < 3) espera.push(nombre);
    return true;
  }

  function activar(valor) {
    activos = valor;
    guardar(valor);
    mostrar();
    if (valor) for (const nombre of NOMBRES) audio(nombre).load();
  }

  function mostrar() {
    if (!boton) return;
    boton.setAttribute("aria-pressed", String(activos));
    boton.title = activos ? "Silenciar avisos sonoros" : "Activar avisos sonoros";
  }

  boton?.addEventListener("click", () => {
    // Activados pero bloqueados por el navegador: este clic ya los habilita, así que no se silencian.
    const bloqueado = boton.hasAttribute("data-bloqueado");
    boton.removeAttribute("data-bloqueado");
    activar(bloqueado || !activos);
    if (activos) reproducir("caso-asignado");
  });
  // Cualquier clic o tecla en la página ya permite el sonido (en el propio botón lo resuelve su clic).
  for (const evento of ["pointerdown", "keydown"])
    document.addEventListener(evento, (e) => {
      if (!boton?.contains(e.target)) boton?.removeAttribute("data-bloqueado");
    }, { capture: true });

  mostrar();
  window.CadSonidos = { reproducir, activar, get activos() { return activos; } };

  // El resultado de la acción anterior suena una vez: el que dejó el servidor o, si la página trae un error, el de error o,
  // si trae la confirmación de una operación, el de operación confirmada (solo con el botón de avisos sonoros: el del
  // operador; el administrador no tiene cómo silenciarlo).
  const pendiente = document.body.dataset.sonido
    || (document.querySelector(".aviso--error[data-enfocar]") ? "error" : "")
    || ((boton || enMarco) && document.querySelector(".aviso--exito[data-enfocar]") ? "operacion-confirmada" : "");
  if (pendiente) reproducir(pendiente);
})();
