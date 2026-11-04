// HU47 (S6-T13) — «Usar este motivo»: copia la decisión sugerida al formulario «Decidir» (tipo, motivo y, según la decisión, la
// prioridad, el destino y la acción, o el motivo del descarte). No registra nada (CA02): el operador revisa, edita y pulsa
// «Registrar la decisión». Delegado en el documento: la tarjeta se reemplaza en vivo cuando llega la sugerencia (consola.js).
(() => {
  const campos = [
    ["sugerenciaMotivo", "Decision_Motivo"],
    ["sugerenciaPrioridad", "Decision_Prioridad"],
    ["sugerenciaDestino", "Decision_Destino"],
    ["sugerenciaAccion", "Decision_Accion"],
    ["sugerenciaMotivoDescarte", "Decision_MotivoDescarte"],
  ];

  const fijar = (campo, valor) => {
    if (!campo || !valor) return;
    campo.value = valor;
    campo.dispatchEvent(new Event("input", { bubbles: true }));
    campo.dispatchEvent(new Event("change", { bubbles: true }));
  };

  document.addEventListener("click", (evento) => {
    const boton = evento.target.closest("[data-usar-motivo]");
    if (!boton) return;
    const datos = boton.dataset;
    const tipo = document.querySelector(`input[name="Decision.Tipo"][value="${CSS.escape(datos.sugerenciaTipo ?? "")}"]`);
    if (!tipo) return;
    tipo.checked = true;
    tipo.dispatchEvent(new Event("change", { bubbles: true }));
    for (const [dato, id] of campos) fijar(document.getElementById(id), datos[dato]);

    const motivo = document.getElementById("Decision_Motivo");
    motivo?.closest("form")?.scrollIntoView({ block: "start", behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth" });
    motivo?.focus({ preventScroll: true });
    const anuncio = document.querySelector("[data-anuncio]");
    if (anuncio) anuncio.textContent = "La sugerencia se copió al formulario de la decisión. Revísala antes de registrarla.";
  });
})();
