// «Reportes y SOS falsos» (16/11/2026): las pestañas «Reportes» y «SOS» del tablero. Cambian el titular, la lectura y el dibujo, y
// repiten la entrada del que se muestra (los íconos caen, el falso gira, los puntos se deslizan). Sin JavaScript las pestañas
// quedan ocultas y se ve la vista de reportes; los SOS siguen en su tarjeta de abajo.
(() => {
  const tablero = document.querySelector("[data-falsos]");
  const pestanas = tablero?.querySelector("[data-pestanas]");
  if (!pestanas) return;
  const botones = [...pestanas.querySelectorAll("[data-ver]")];
  const reducido = matchMedia("(prefers-reduced-motion: reduce)");

  function ver(vista) {
    for (const boton of botones) boton.setAttribute("aria-pressed", String(boton.dataset.ver === vista));
    for (const parte of tablero.querySelectorAll("[data-vista]")) parte.hidden = parte.dataset.vista !== vista;
    if (reducido.matches) return;
    // Las animaciones de CSS vuelven a empezar: se cancelan y se reproducen desde 0, cada una con su retraso.
    for (const parte of tablero.querySelectorAll(`.falsos-b__tablero[data-vista="${vista}"]`))
      for (const animacion of parte.getAnimations({ subtree: true })) { animacion.cancel(); animacion.play(); }
  }

  for (const boton of botones) boton.addEventListener("click", () => ver(boton.dataset.ver));
  pestanas.hidden = false;
})();
