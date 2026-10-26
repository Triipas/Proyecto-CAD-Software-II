// Movimiento del panel (26/10/2026). Las duraciones y curvas se leen de los tokens (tokens.css): con movimiento reducido las
// duraciones valen 0 y todo es inmediato.
//
// actualizarRegion reemplaza el contenido de una región con el HTML que llegó del servidor sin rehacerla entera: conserva los
// nodos que siguen ahí (el foco, la selección y el desplazamiento no se pierden) y solo cambia lo que cambió. Los elementos con
// data-clave se reconocen aunque cambien de lugar; con { animar: true }, los que entran suben 8 px y los que se mueven se
// deslizan a su sitio nuevo (FLIP). Devuelve las claves que entraron y las que se fueron.
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

  function actualizarRegion(region, html, { animar = false } = {}) {
    const antes = new Map();
    for (const el of region.querySelectorAll("[data-clave]")) antes.set(el.getAttribute("data-clave"), animar ? el.getBoundingClientRect() : null);
    enfocado = region.contains(document.activeElement) ? document.activeElement : null;
    const desplazamiento = region.scrollTop;

    const plantilla = document.createElement("template");
    plantilla.innerHTML = html;
    parcharHijos(region, plantilla.content);

    if (enfocado && enfocado.isConnected && document.activeElement !== enfocado) enfocado.focus({ preventScroll: true });
    enfocado = null;
    if (region.scrollTop !== desplazamiento) region.scrollTop = desplazamiento;

    const ahora = [...region.querySelectorAll("[data-clave]")];
    const claves = new Set(ahora.map((el) => el.getAttribute("data-clave")));
    const nuevos = ahora.filter((el) => !antes.has(el.getAttribute("data-clave")));
    const quitados = [...antes.keys()].filter((c) => !claves.has(c));

    if (animar && duracion("base") > 0) {
      for (const el of exteriores(nuevos)) {
        el.animate([{ opacity: 0, transform: "translateY(8px)" }, { opacity: 1, transform: "none" }],
          { duration: duracion("base"), easing: curva("salida"), fill: "backwards" });
      }
      const movidos = [];
      for (const el of ahora) {
        const previo = antes.get(el.getAttribute("data-clave"));
        if (!previo) continue;
        const actual = el.getBoundingClientRect();
        const dx = previo.left - actual.left, dy = previo.top - actual.top;
        if (Math.abs(dx) > 0.5 || Math.abs(dy) > 0.5) movidos.push({ el, dx, dy });
      }
      const exteriorMovido = new Set(exteriores(movidos.map((m) => m.el)));
      for (const { el, dx, dy } of movidos) {
        if (!exteriorMovido.has(el)) continue;
        el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "none" }],
          { duration: duracion("base"), easing: curva("movimiento") });
      }
    }

    return { nuevos: nuevos.map((el) => el.getAttribute("data-clave")), quitados };
  }

  window.cadMovimiento = { reducido: () => reducido.matches, duracion, curva, actualizarRegion };
})();
