// Movimiento del panel (26/10/2026). Las duraciones y curvas se leen de los tokens (tokens.css): con movimiento reducido las
// duraciones valen 0 y todo es inmediato.
//
// actualizarRegion reemplaza el contenido de una región con el HTML que llegó del servidor sin rehacerla entera: conserva los
// nodos que siguen ahí (el foco, la selección y el desplazamiento no se pierden) y solo cambia lo que cambió. Los elementos con
// data-clave se reconocen aunque cambien de lugar. Con { animar: true }:
// - los que entran suben 8 px; los que se van se desvanecen en su sitio; los que se mueven se deslizan a su sitio nuevo (FLIP);
// - lo que está bajo el cursor o con el foco se queda donde estaba (se compensa el desplazamiento del contenedor), para que el
//   operador no pulse otra tarjeta porque la lista se movió;
// - un elemento con data-suave cuyo texto cambió (un chip de estado) aparece de nuevo con un fundido corto.
// «despues» corre justo tras el cambio y antes de medir: ahí se vuelven a poner las clases que solo conoce el navegador
// (seleccionado, nuevo), sin que lleguen a quitarse en pantalla. Devuelve las claves que entraron y las que se fueron.
(() => {
  const reducido = matchMedia("(prefers-reduced-motion: reduce)");
  const raiz = getComputedStyle(document.documentElement);

  function duracion(nombre) {
    if (reducido.matches) return 0;
    const valor = raiz.getPropertyValue(`--duracion-${nombre}`).trim();
    return valor.endsWith("ms") ? parseFloat(valor) : parseFloat(valor) * 1000 || 0;
  }

  function curva(nombre) {
    return raiz.getPropertyValue(`--curva-${nombre}`).trim() || "ease";
  }

  // Una cifra que cambia late una vez (solo escala).
  function fijarCifra(elemento, valor) {
    if (!elemento || elemento.textContent === String(valor)) return;
    elemento.textContent = valor;
    if (duracion("lenta") > 0)
      elemento.animate([{ transform: "scale(1.25)" }, { transform: "none" }], { duration: duracion("lenta"), easing: curva("salida") });
  }

  // El elemento con foco al empezar: moverlo en el DOM le quita el foco, pero sigue siendo lo que el operador está usando.
  let enfocado = null;

  const clave = (nodo) => (nodo.nodeType === Node.ELEMENT_NODE ? nodo.getAttribute("data-clave") : null);
  const mismoTipo = (a, b) => a.nodeType === b.nodeType && a.nodeName === b.nodeName && clave(a) === clave(b);

  function parchar(viejo, nuevo) {
    if (viejo.nodeType !== Node.ELEMENT_NODE) {
      if (viejo.nodeValue !== nuevo.nodeValue) viejo.nodeValue = nuevo.nodeValue;
      return;
    }
    for (const { name } of [...viejo.attributes]) if (!nuevo.hasAttribute(name)) viejo.removeAttribute(name);
    for (const { name, value } of nuevo.attributes) if (viejo.getAttribute(name) !== value) viejo.setAttribute(name, value);
    // Lo que el operador está escribiendo o eligiendo no se pisa.
    if ((viejo instanceof HTMLInputElement || viejo instanceof HTMLTextAreaElement || viejo instanceof HTMLSelectElement) && viejo !== enfocado) {
      if (viejo.value !== nuevo.value) viejo.value = nuevo.value;
      if (viejo instanceof HTMLInputElement) viejo.checked = nuevo.checked;
    }
    if (viejo instanceof HTMLTextAreaElement) return;
    parcharHijos(viejo, nuevo);
  }

  function parcharHijos(padre, modelo) {
    const conClave = new Map();
    for (const hijo of padre.childNodes) {
      const c = clave(hijo);
      if (c) conClave.set(c, hijo);
    }
    let actual = padre.firstChild;
    for (const nuevo of [...modelo.childNodes]) {
      const c = clave(nuevo);
      let existente = null;
      if (c) {
        existente = conClave.get(c) ?? null;
        conClave.delete(c);
      } else if (actual && !clave(actual) && mismoTipo(actual, nuevo)) {
        existente = actual;
      }
      if (!existente) {
        padre.insertBefore(nuevo, actual);
        continue;
      }
      if (existente === actual) actual = actual.nextSibling;
      else padre.insertBefore(existente, actual);
      parchar(existente, nuevo);
    }
    while (actual) {
      const siguiente = actual.nextSibling;
      padre.removeChild(actual);
      actual = siguiente;
    }
  }

  // Solo los de más afuera: si entra o se mueve un grupo, sus elementos van con él.
  const exteriores = (elementos) => elementos.filter((el) => !elementos.some((otro) => otro !== el && otro.contains(el)));

  function contenedorDesplazable(elemento) {
    for (let el = elemento.parentElement; el; el = el.parentElement) {
      const { overflowY } = getComputedStyle(el);
      if ((overflowY === "auto" || overflowY === "scroll") && el.scrollHeight > el.clientHeight) return el;
    }
    return document.scrollingElement;
  }

  // Lo que se fue se desvanece en su sitio: una copia inerte (sin id ni data-*, para que nadie la confunda con un caso vivo)
  // ocupa su lugar sin empujar a nadie y se retira sola.
  function despedir(viejo, padre, caja) {
    if (!padre.isConnected) return;
    const copia = viejo.cloneNode(true);
    for (const el of [copia, ...copia.querySelectorAll("*")]) {
      for (const { name } of [...el.attributes]) if (name === "id" || name.startsWith("data-") || name.startsWith("aria-")) el.removeAttribute(name);
    }
    copia.setAttribute("aria-hidden", "true");
    copia.inert = true;
    Object.assign(copia.style, { position: "absolute", margin: "0", width: `${caja.width}px`, height: `${caja.height}px`, pointerEvents: "none" });
    padre.appendChild(copia);
    const aqui = copia.getBoundingClientRect();
    const sitio = `translate(${caja.left - aqui.left}px, ${caja.top - aqui.top}px)`;
    copia.animate([{ opacity: 1, transform: sitio }, { opacity: 0, transform: `${sitio} scale(0.98)` }],
      { duration: duracion("rapida"), easing: curva("entrada"), fill: "forwards" }).finished.finally(() => copia.remove());
  }

  function actualizarRegion(region, html, { animar = false, despues = null } = {}) {
    const animando = animar && duracion("base") > 0;
    const antes = new Map();
    for (const el of region.querySelectorAll("[data-clave]"))
      antes.set(el.getAttribute("data-clave"), animando ? { el, padre: el.parentElement, caja: el.getBoundingClientRect() } : { el });
    const suaves = new Map(animando ? [...region.querySelectorAll("[data-suave]")].map((el) => [el, el.textContent]) : []);
    enfocado = region.contains(document.activeElement) ? document.activeElement : null;
    const desplazamiento = region.scrollTop;
    // El ancla: la tarjeta bajo el cursor o, si no, la que tiene el foco.
    const bajoCursor = [...region.querySelectorAll("[data-clave]:hover")].at(-1);
    const ancla = animar ? bajoCursor ?? enfocado?.closest("[data-clave]") : null;
    const altoAncla = ancla?.getBoundingClientRect().top;

    const plantilla = document.createElement("template");
    plantilla.innerHTML = html;
    parcharHijos(region, plantilla.content);
    despues?.();

    if (enfocado && enfocado.isConnected && document.activeElement !== enfocado) enfocado.focus({ preventScroll: true });
    enfocado = null;
    if (region.scrollTop !== desplazamiento) region.scrollTop = desplazamiento;
    if (ancla?.isConnected) {
      const deriva = ancla.getBoundingClientRect().top - altoAncla;
      if (Math.abs(deriva) > 0.5) contenedorDesplazable(ancla).scrollTop += deriva;
    }

    const ahora = [...region.querySelectorAll("[data-clave]")];
    const claves = new Set(ahora.map((el) => el.getAttribute("data-clave")));
    const nuevos = ahora.filter((el) => !antes.has(el.getAttribute("data-clave")));
    const quitados = [...antes.keys()].filter((c) => !claves.has(c));

    if (animando) {
      for (const el of exteriores(nuevos)) {
        el.animate([{ opacity: 0, transform: "translateY(8px)" }, { opacity: 1, transform: "none" }],
          { duration: duracion("base"), easing: curva("salida"), fill: "backwards" });
      }
      const idos = quitados.map((c) => antes.get(c));
      const exteriorIdo = new Set(exteriores(idos.map((i) => i.el)));
      for (const { el, padre, caja } of idos) if (exteriorIdo.has(el)) despedir(el, padre, caja);

      const movidos = [];
      for (const el of ahora) {
        const previo = antes.get(el.getAttribute("data-clave"));
        if (!previo) continue;
        const actual = el.getBoundingClientRect();
        const dx = previo.caja.left - actual.left, dy = previo.caja.top - actual.top;
        if (Math.abs(dx) > 0.5 || Math.abs(dy) > 0.5) movidos.push({ el, dx, dy });
      }
      const exteriorMovido = new Set(exteriores(movidos.map((m) => m.el)));
      for (const { el, dx, dy } of movidos) {
        if (!exteriorMovido.has(el)) continue;
        el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "none" }],
          { duration: duracion("base"), easing: curva("movimiento") });
      }

      for (const [el, texto] of suaves) {
        if (!el.isConnected || el.textContent === texto) continue;
        el.animate([{ opacity: 0.2 }, { opacity: 1 }], { duration: duracion("base"), easing: curva("estandar") });
      }
    }

    return { nuevos: nuevos.map((el) => el.getAttribute("data-clave")), quitados };
  }

  window.cadMovimiento = { reducido: () => reducido.matches, duracion, curva, fijarCifra, actualizarRegion };
})();
