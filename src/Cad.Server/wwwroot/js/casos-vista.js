// Casos (HU26), rediseño del 05/11/2026: elegir un caso de la lista muestra su resumen en el panel de la derecha (desde 1200 px); en
// pantallas estrechas, la fila abre el caso como antes. El resumen llega del servidor (?handler=Vista&id=…), ya armado.
(() => {
  const tabla = document.querySelector("[data-casos]");
  const panel = document.querySelector("[data-vista-previa]");
  const ancha = matchMedia("(min-width: 75rem)");
  const vacio = panel?.innerHTML ?? "";
  let elegida = null;
  let pedido = 0;

  // «Más filtros» es un panel flotante: se cierra al elegir otra cosa o con Escape.
  const mas = document.querySelector("[data-panel-filtros]");
  document.addEventListener("click", (e) => {
    if (mas?.open && !mas.contains(e.target)) mas.open = false;
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && mas?.open) {
      mas.open = false;
      mas.querySelector("summary")?.focus();
    }
  });

  if (!tabla || !panel) return;

  const filas = () => [...tabla.querySelectorAll("tr[data-caso]")];

  function marcar(fila) {
    for (const f of filas()) {
      const activa = f === fila;
      f.classList.toggle("casos__fila--elegida", activa);
      f.querySelector("[data-ver-caso]")?.setAttribute("aria-pressed", activa ? "true" : "false");
    }
  }

  function limpiar() {
    elegida = null;
    pedido++;
    marcar(null);
    panel.removeAttribute("aria-busy");
    panel.innerHTML = vacio;
  }

  async function elegir(fila) {
    if (fila === elegida) return;
    elegida = fila;
    marcar(fila);
    const mio = ++pedido;
    panel.setAttribute("aria-busy", "true");
    try {
      const respuesta = await fetch(`/panel/casos?handler=Vista&id=${encodeURIComponent(fila.dataset.caso)}`, { credentials: "same-origin" });
      if (mio !== pedido) return;
      if (!respuesta.ok) throw new Error(String(respuesta.status));
      panel.innerHTML = await respuesta.text();
    } catch {
      if (mio !== pedido) return;
      // Sin resumen no se pierde nada: el caso se abre desde la fila.
      panel.innerHTML = '<div class="vista-vacia" role="alert"><p class="vista-vacia__titulo">No se pudo cargar el resumen</p><p>Abre el caso desde la fila o vuelve a elegirlo.</p></div>';
      elegida = null;
    } finally {
      if (mio === pedido) panel.removeAttribute("aria-busy");
    }
  }

  // Ancho: la fila elige el caso para el panel. Estrecho: la fila abre el caso (los enlaces siguen siendo el camino con teclado).
  tabla.addEventListener("click", (e) => {
    if (e.target.closest("a") || window.getSelection()?.toString()) return;
    const fila = e.target.closest("tr[data-caso]");
    if (!fila) return;
    if (ancha.matches) elegir(fila);
    else fila.querySelector("[data-abrir-caso]")?.click();
  });

  // Con el teclado: flechas arriba y abajo pasan de un caso a otro; Escape deja el panel vacío.
  tabla.addEventListener("keydown", (e) => {
    const boton = e.target.closest("[data-ver-caso]");
    if (!boton) return;
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      const lista = filas();
      const siguiente = lista[lista.indexOf(boton.closest("tr")) + (e.key === "ArrowDown" ? 1 : -1)];
      if (siguiente) {
        e.preventDefault();
        siguiente.querySelector("[data-ver-caso]").focus();
      }
    } else if (e.key === "Escape" && elegida) {
      limpiar();
    }
  });

  // Si la pantalla se estrecha, el panel se oculta: se deja listo para cuando vuelva.
  ancha.addEventListener("change", () => {
    if (!ancha.matches) limpiar();
  });
})();
