// Consola de incidente (27/10/2026): mantiene al día «hace N min» de la barra del caso sin recargar la página. El servidor ya
// escribe el valor inicial; esto solo lo avanza cada 30 s con el mismo formato.
(() => {
  const elementos = document.querySelectorAll("[data-transcurrido]");
  if (elementos.length === 0) return;

  const texto = (ms) => {
    const minutos = Math.floor(ms / 60000);
    if (minutos < 1) return "hace menos de 1 min";
    if (minutos < 60) return `hace ${minutos} min`;
    const horas = Math.floor(minutos / 60);
    if (horas < 24) return `hace ${horas} h ${minutos % 60} min`;
    return `hace ${Math.floor(horas / 24)} d ${horas % 24} h`;
  };

  const actualizar = () => {
    const ahora = Date.now();
    for (const elemento of elementos) {
      const desde = Date.parse(elemento.dataset.transcurrido);
      if (!Number.isNaN(desde)) elemento.textContent = texto(ahora - desde);
    }
  };

  setInterval(actualizar, 30000);
})();
