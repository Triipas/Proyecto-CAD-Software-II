// Mejora progresiva de /panel/ingresar: sin JavaScript el formulario funciona igual.
(() => {
  // Tras un intento fallido, el foco va al aviso para que se lea primero (role="alert").
  document.querySelector("[data-enfocar]")?.focus();

  const formulario = document.querySelector("[data-formulario-ingreso]");
  const boton = formulario?.querySelector("button[type=submit]");
  if (!formulario || !boton) return;
  const textoOriginal = boton.textContent;

  // Estado de carga: evita un segundo envío mientras el servidor responde.
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
})();
