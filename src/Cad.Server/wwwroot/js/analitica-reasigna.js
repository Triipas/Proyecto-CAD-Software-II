// «Reasignaciones» (16/11/2026): pasar por la tarjeta de un motivo (con el cursor o con el teclado) lo resalta en las columnas
// de cada tramo; los demás motivos se apagan. Sin JavaScript se ve todo a la vez, igual que al no señalar ninguno.
(() => {
  const tablero = document.querySelector("[data-arreglar]");
  if (!tablero) return;
  const tarjetas = [...tablero.querySelectorAll(".accion-b[data-motivo]")];
  const partes = [...tablero.querySelectorAll(".columnas-b__pila i[data-motivo]")];

  function resaltar(motivo) {
    if (motivo) tablero.dataset.foco = motivo; else delete tablero.dataset.foco;
    for (const parte of partes) parte.classList.toggle("foco", parte.dataset.motivo === motivo);
    for (const tarjeta of tarjetas) tarjeta.classList.toggle("foco", tarjeta.dataset.motivo === motivo);
  }

  for (const tarjeta of tarjetas) {
    tarjeta.addEventListener("mouseenter", () => resaltar(tarjeta.dataset.motivo));
    tarjeta.addEventListener("focus", () => resaltar(tarjeta.dataset.motivo));
    tarjeta.addEventListener("mouseleave", () => resaltar(null));
    tarjeta.addEventListener("blur", () => resaltar(null));
  }
})();
