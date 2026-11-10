// «Tiempos de respuesta»: la carrera de un SOS típico (14/11/2026). Una ficha recorre las etapas (la mediana de cada una) a velocidad
// rápida y constante, con un reloj que cuenta los minutos; cada tramo se llena a su paso y su nombre se destaca. Corre una vez,
// al entrar a la vista, y queda en la meta. Sin JavaScript o con movimiento reducido se ve terminada: los tramos llenos y el reloj
// con el total, tal como llega del servidor.
(() => {
  const carrera = document.querySelector("[data-carrera]");
  if (!carrera || matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  const tramos = [...carrera.querySelectorAll("[data-minutos]")].map((el) => ({ el, relleno: el.querySelector("i"), minutos: parseFloat(el.dataset.minutos) || 0 }));
  const nombres = [...carrera.querySelectorAll("[data-tramo]")];
  const ficha = carrera.querySelector("[data-ficha]");
  const reloj = carrera.querySelector("[data-reloj]");
  const total = tramos.reduce((suma, t) => suma + t.minutos, 0);
  if (!ficha || !reloj || total <= 0) return;

  const MS_POR_MINUTO = 380;   // 12 min de recorrido ≈ 4,5 s
  const ESPERA_INICIAL = 1400; // que termine de entrar el histograma
  function texto(minutos) {
    const segundos = Math.round(minutos * 60);
    return `${String(Math.floor(segundos / 60)).padStart(2, "0")}:${String(segundos % 60).padStart(2, "0")}`;
  }

  function pintar(t) {
    let desde = 0;
    let x = 0;
    tramos.forEach((tramo, i) => {
      const lleno = tramo.minutos === 0 ? (t >= desde ? 1 : 0) : Math.max(0, Math.min(1, (t - desde) / tramo.minutos));
      tramo.relleno.style.width = `${lleno * 100}%`;
      if (t >= desde) x = tramo.el.offsetLeft + lleno * tramo.el.offsetWidth;
      nombres[i]?.classList.toggle("activa", t >= desde && t < desde + tramo.minutos);
      desde += tramo.minutos;
    });
    ficha.style.left = `${x}px`;
    reloj.textContent = texto(Math.min(t, total));
  }

  // Una sola carrera: arranca la primera vez que la carrera está a la vista (pasada la espera inicial) y queda en la meta. Si se
  // oculta la pestaña a mitad de camino, termina al volver. No se repite.
  let visible = false, listo = false, terminada = false, origen = null, avance = 0, cuadro = 0;
  function paso(ahora) {
    if (document.hidden) { avance += (ahora - (origen ?? ahora)) / MS_POR_MINUTO; origen = null; cuadro = 0; return; }
    if (origen === null) origen = ahora;
    const t = avance + (ahora - origen) / MS_POR_MINUTO;
    pintar(Math.min(total, t));
    if (t >= total) { terminada = true; cuadro = 0; return; }
    cuadro = requestAnimationFrame(paso);
  }

  function arrancar() {
    if (!listo || terminada || cuadro || !visible || document.hidden) return;
    cuadro = requestAnimationFrame(paso);
  }

  pintar(0);
  const observador = new IntersectionObserver(([entrada]) => {
    visible = entrada.isIntersecting;
    arrancar();
    if (terminada) observador.disconnect();
  });
  observador.observe(carrera);
  document.addEventListener("visibilitychange", arrancar);
  // Hasta la espera inicial, la ficha queda en la salida aunque la carrera ya esté a la vista.
  setTimeout(() => { listo = true; arrancar(); }, ESPERA_INICIAL);
})();
