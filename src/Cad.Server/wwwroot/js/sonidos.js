// Sonidos del panel (24/10/2026). Los navegadores solo permiten sonido tras una acción del usuario (Plan de Arquitectura,
// SP-E): el operador los activa una vez desde la central y la preferencia queda en este navegador. Sin activarlos, nada suena:
// cada aviso sonoro tiene siempre su aviso visual.
(() => {
  const CLAVE = "cad.avisosSonoros";
  const NOMBRES = ["sos-nuevo", "reporte-admitido", "caso-asignado", "aviso-acompanamiento", "error"];
  const audios = new Map();
  let activos = leer();

  function leer() {
    try { return localStorage.getItem(CLAVE) === "1"; } catch { return false; }
  }
  function guardar(valor) {
    try { valor ? localStorage.setItem(CLAVE, "1") : localStorage.removeItem(CLAVE); } catch { /* sin almacenamiento: dura la página */ }
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

  // Devuelve si se intentó reproducir (avisos activados). Si el navegador lo bloquea (aún no hubo interacción con la página),
  // lo dice con «cad:sonido-bloqueado» para pedir un clic.
  function reproducir(nombre) {
    if (!activos) return false;
    const a = audio(nombre);
    a.currentTime = 0;
    a.play().catch((error) => {
      if (error.name === "NotAllowedError") document.dispatchEvent(new CustomEvent("cad:sonido-bloqueado"));
    });
    return true;
  }

  function activar(valor) {
    activos = valor;
    guardar(valor);
    if (valor) for (const nombre of NOMBRES) audio(nombre).load();
  }

  window.CadSonidos = { reproducir, activar, get activos() { return activos; } };

  // El resultado de la acción anterior suena una vez: el que dejó el servidor o, si la página trae un error, el de error.
  const pendiente = document.body.dataset.sonido || (document.querySelector(".aviso--error[data-enfocar]") ? "error" : "");
  if (pendiente) reproducir(pendiente);
})();
