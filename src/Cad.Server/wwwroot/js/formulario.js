// Mejora progresiva de los formularios del panel: sin JavaScript funcionan igual.
(() => {
  // Tras un envío rechazado, el foco va al primer aviso o error marcado con data-enfocar.
  document.querySelector("[data-enfocar]")?.focus();

  // Estado de carga: evita un segundo envío mientras el servidor responde.
  for (const formulario of document.querySelectorAll("form[data-formulario]")) {
    const boton = formulario.querySelector("button[type=submit][data-texto-carga]");
    if (!boton) continue;
    const textoOriginal = boton.textContent;

    formulario.addEventListener("submit", (evento) => {
      if (formulario.getAttribute("aria-busy") === "true") {
        evento.preventDefault();
        return;
      }
      formulario.setAttribute("aria-busy", "true");
      boton.setAttribute("aria-disabled", "true");
      boton.textContent = boton.dataset.textoCarga;
    });

    // Al volver con «Atrás», el navegador puede restaurar la página en estado de carga.
    window.addEventListener("pageshow", () => {
      formulario.removeAttribute("aria-busy");
      boton.removeAttribute("aria-disabled");
      boton.textContent = textoOriginal;
    });
  }
})();
