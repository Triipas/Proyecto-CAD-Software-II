// Llamar desde el detalle del SOS (26/10/2026). El enlace tel: abre el teléfono o el programa de llamadas del operador: la
// llamada no pasa por el sistema. Al pulsarlo se anota la hora junto al formulario y el foco pasa al resultado del intento,
// para registrarlo apenas termine la llamada.
(() => {
  for (const enlace of document.querySelectorAll("[data-llamar]")) {
    enlace.addEventListener("click", () => {
      const estado = enlace.closest("[data-llamada]")?.querySelector("[data-llamada-estado]");
      if (!estado) return;
      const hora = new Date().toLocaleTimeString("es-PE", { hour: "2-digit", minute: "2-digit", hour12: false });
      const quien = enlace.dataset.llamar === "contacto" ? `${enlace.dataset.nombre}, su contacto de emergencia,` : enlace.dataset.nombre;
      estado.textContent = `Llamaste a ${quien} a las ${hora}. Cuando termines, registra el resultado del intento.`;
      estado.hidden = false;
      enlace.closest("form")?.querySelector('input[name="Formulario.Resultado"]')?.focus({ preventScroll: true });
    });
  }
})();
