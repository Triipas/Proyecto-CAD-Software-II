// Sonidos del panel (24/10/2026). Activados por defecto; el operador los silencia con el botón de la barra superior y la
// preferencia queda en este navegador. Cada aviso sonoro tiene siempre su aviso visual.
(() => {
  const CLAVE = "cad.avisosSonoros";
  const NOMBRES = ["sos-nuevo", "reporte-admitido", "caso-asignado", "aviso-acompanamiento", "error"];
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
      a = new Audio(`/sonidos/${nombre}.wav`);
      a.preload = "auto";
      audios.set(nombre, a);
    }
    return a;
  }

  // Devuelve si se intentó reproducir (avisos activados). Los navegadores bloquean el sonido hasta que haya una acción del
  // usuario en la página (Plan de Arquitectura, SP-E): entonces el botón lo señala y el primer clic lo habilita.
  function reproducir(nombre) {
    if (!activos) return false;
    const a = audio(nombre);
    a.currentTime = 0;
    a.play().catch((error) => {
      if (error.name === "NotAllowedError") boton?.setAttribute("data-bloqueado", "");
    });
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

  // El resultado de la acción anterior suena una vez: el que dejó el servidor o, si la página trae un error, el de error.
  const pendiente = document.body.dataset.sonido || (document.querySelector(".aviso--error[data-enfocar]") ? "error" : "");
  if (pendiente) reproducir(pendiente);
})();
