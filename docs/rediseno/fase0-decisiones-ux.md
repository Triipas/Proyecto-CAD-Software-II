# Fase 0 · Decisiones de UX por rol

Decisiones de estructura tomadas con las skills `ux-first-principles`, `ux-desktop` y `ux-mobile`, a partir de la [crítica](fase0-critica.md). Cada decisión indica los principios en juego, la tensión entre ellos y la elección. Son decisiones de **estructura**: el aspecto (color, tipo, sombras) lo define la fase 1.

Regla general: los principios no cambian entre web y móvil; cambia el **medio**. El panel web es escritorio (puntero preciso, teclado, hover, alta densidad). La app es teléfono (dedo, sin hover, una mano, una tarea a la vez).

---

## Operador de central (escritorio)

### D1 · Navegación lateral en lugar de barra superior
> **Sustituida en la fase 2** (18/10/2026): con la dirección «Mapa protagonista», el panel usa una barra superior marino con secciones e indicadores en vivo. Se mantiene la idea de los contadores como indicador de estado. Ver el [plan](../plan-rediseno.md).

- **Principios:** reconocimiento en vez de recuerdo, ley de Jakob (los paneles de operación usan barra lateral), visibilidad del estado del sistema.
- **Tensión:** la barra superior ocupa menos ancho; la lateral consume ~240 px que el mapa querría.
- **Decisión:** barra lateral **colapsable** (240 px ↔ 72 px solo iconos) con contadores en vivo junto a cada sección (SOS pendientes, avisos sin resolver). Los contadores convierten la navegación en un indicador de estado: el operador sabe dónde hay trabajo sin entrar. En ≤ 1024 px pasa a cajón con botón de menú.

### D2 · Central en tres zonas sin scroll
> **Ajustada en la fase 2**: las tres zonas se mantienen, pero el mapa ocupa toda la pantalla y la cola y los serenos flotan encima como paneles plegables.

- **Principios:** jerarquía visual, región común (Gestalt), ley de Fitts (distancia entre caso y sereno), densidad alta aceptable en escritorio.
- **Tensión:** el mapa necesita superficie; la cola y los serenos necesitan estar visibles a la vez.
- **Decisión:** franja de indicadores arriba + **cola de trabajo** (izquierda, ~380 px) + **mapa** (centro, flexible) + **serenos** (derecha, ~300 px). A 1366 × 768 todo cabe sin scroll de página; cada columna hace scroll interno. Por debajo de 1280 px, los serenos pasan a una pestaña dentro de la cola.

### D3 · La cola separa por pestañas, pero los SOS nunca se ocultan
- **Principios:** ley de Hick, efecto Von Restorff (lo distinto se recuerda), prevención de errores.
- **Tensión:** agrupar SOS, reportes y acompañamientos en pestañas reduce ruido, pero un SOS escondido en una pestaña no activa es un riesgo.
- **Decisión:** pestañas *Reportes · Acompañamientos* en la cola, **y los SOS pendientes siempre fijados arriba** de cualquier pestaña, con borde rojo e insignia. Un SOS nuevo además anima su entrada y pulsa en el mapa (la única animación persistente del sistema).

### D4 · Asignar desde un diálogo modal
- **Principios:** prevención de errores (Nielsen 5), restricciones (Norman), proximidad de la confirmación a la acción.
- **Tensión:** un modal interrumpe; un banner en la parte superior (actual, `web/038`) no interrumpe pero está lejos de lo que se confirma.
- **Decisión:** **modal** centrado con resumen del caso y del sereno (nombre, distancia aproximada, última posición) y botones «Cancelar» / «Confirmar asignación». Asignar es una acción con consecuencias en la calle: merece la interrupción. Foco atrapado; `Esc` cancela; `Enter` confirma.

### D5 · Detalle de caso guiado por «Siguiente acción»
- **Principios:** efecto Goal-Gradient y Zeigarnik (mostrar el progreso motiva a completarlo), visibilidad del estado, divulgación progresiva.
- **Tensión:** el operador experto quiere ver todos los datos; el operador nuevo necesita saber qué hacer.
- **Decisión:** encabezado con **línea de progreso** del caso; columna principal con una única tarjeta **«Siguiente acción»** (verificar → clasificar → asignar → cerrar) y debajo el historial de intentos; columna lateral con el contexto (ciudadano, ubicación con minimapa, tiempos). Las acciones aún no disponibles se explican en una línea dentro de la línea de progreso, no en tarjetas propias.

### D6 · El texto de ayuda pasa a *hover* y foco
- **Principios:** carga cognitiva, estética y diseño minimalista (Nielsen 8); en escritorio el *hover* es legítimo.
- **Tensión:** los textos actuales son precisos y evitan errores de interpretación; quitarlos puede generar dudas.
- **Decisión:** cada texto explicativo se reduce a **una línea** y el detalle va a un icono «i» con tooltip que se abre **con hover y con foco de teclado** (nunca solo hover). La leyenda del mapa pasa a chips interactivos que además filtran capas.

### D7 · Tablas densas pero escaneables
- **Principios:** similitud (Gestalt), alineación, densidad alta en escritorio, operabilidad con teclado.
- **Decisión:** chips de estado con icono; fila completa pulsable **y** enlace «Abrir» visible para teclado y lectores de pantalla; números alineados a la derecha con cifras tabulares; filtros en una barra plegable con chips de filtros activos y «Limpiar».

### D8 · Atajos de teclado para la central
- **Principios:** flexibilidad y eficiencia de uso (Nielsen 7), atajos para usuarios frecuentes (Shneiderman 2).
- **Decisión:** `J`/`K` para moverse en la cola, `Enter` para abrir, `A` para asignar el caso seleccionado, `?` para ver la lista. Siempre con alternativa visible por botón.

## Administrador (escritorio)

### D9 · Prevención de errores por encima de la velocidad
- **Principios:** prevención de errores, reconocimiento sobre recuerdo, uso poco frecuente.
- **Decisión:** el administrador entra pocas veces; prima la claridad. Inicio con tarjetas de resumen; tabla de cuentas con buscador y filtros por rol/estado; formularios en una columna; **desactivar** siempre con modal que explica la consecuencia concreta (p. ej. el despacho vigente que vuelve a la central). Mismo shell lateral que el operador, con sus propias secciones.

---

## Ciudadano (teléfono)

### D10 · Barra de navegación inferior con cuatro pestañas
- **Principios:** ley de Hick (pocas opciones), zonas del pulgar, reconocimiento sobre recuerdo.
- **Tensión:** un cajón lateral cabe más secciones; una barra inferior es visible y alcanzable.
- **Decisión:** **barra inferior** *Inicio · Mis casos · Avisos · Cuenta* (insignia de no leídos en Avisos). Cuatro secciones caben sin cajón y quedan en la zona cómoda del pulgar. «Salir» va dentro de *Cuenta* (acción rara, fuera del alcance accidental).

### D11 · El SOS sigue siendo un toque, más grande y más abajo
- **Principios:** ley de Fitts (objetivo grande), zonas del pulgar, prevención de errores frente a rapidez.
- **Tensión:** sin confirmación hay riesgo de envío accidental; con confirmación se retrasa una emergencia real. El requisito actual es «sin confirmación».
- **Decisión:** se mantiene **un toque sin confirmación** (requisito del dominio). El botón crece y baja al tercio central de la pantalla (alcanzable con el pulgar). El error accidental se compensa después: confirmación visual inmediata y botón «Cancelar SOS» visible en la pantalla de resultado (flujo ya existente).

### D12 · Entradas de flujo como mosaicos
- **Principios:** affordance y significantes (Norman), similitud.
- **Decisión:** «Reportar una incidencia» y «Acompañamiento» pasan de botones contorneados genéricos a **mosaicos con icono, título y una línea**, claramente subordinados al SOS. Son entradas a un flujo, no acciones inmediatas.

### D13 · Tarjetas de caso pulsables y línea de tiempo
- **Principios:** proximidad, eliminación de redundancia, visibilidad del estado.
- **Decisión:** en *Mis casos*, la tarjeta entera es el objetivo táctil (con chevron); se eliminan los botones «Ver avances». En el detalle, los avances se muestran como **línea de tiempo vertical** con el estado actual resaltado; la evaluación aparece como hoja inferior al cerrarse el caso.

### D14 · Reporte en tres secciones y selector segmentado
- **Principios:** divulgación progresiva, ley de Miller (agrupar), tareas de un solo foco.
- **Decisión:** formulario en tres bloques *Qué · Cuándo · Dónde*; para «Dónde», un **selector segmentado** (Mapa · Mi posición · Referencia) que muestra solo el control elegido, en lugar de tres botones apilados. Botón «Enviar reporte» fijo abajo.

### D15 · Ingreso sin detalles técnicos
- **Principios:** estética y diseño minimalista, correspondencia con el mundo real (Nielsen 2).
- **Decisión:** dirección del servidor y diagnóstico de ubicación en un bloque plegable «Opciones de desarrollo», **solo en compilaciones de depuración**.

## Sereno (teléfono)

### D16 · Una sola pantalla de trabajo, sin pestañas
- **Principios:** tarea única, ley de Hick, uso en movimiento.
- **Decisión:** el sereno no tiene barra inferior: su app es **una pantalla** con el estado operativo arriba y el despacho como contenido principal. El cierre del caso se abre como pantalla aparte.

### D17 · Acción principal fija abajo y progresiva
- **Principios:** zonas del pulgar, Goal-Gradient, visibilidad del estado.
- **Decisión:** un único **botón fijo en la parte inferior** cuya etiqueta avanza con el caso: *Voy en camino → Inicié la atención → Cerrar el caso*. Encima, una mini línea de progreso de tres pasos. El identificador del caso, las coordenadas y la información de la central van plegados en «Detalles».

### D18 · Estado operativo como interruptor
- **Principios:** correspondencia con el mundo real, visibilidad del estado, affordance.
- **Decisión:** el estado operativo se muestra como **interruptor grande** en el encabezado (con texto «Operativo»/«No operativo» y hora de la última posición), en lugar de una caja verde más un botón azul. «Salir» pasa al menú del encabezado, lejos del pulgar.

---

## Accesibilidad como restricción en todas las decisiones

- Nada esencial depende solo del *hover* (web) ni de gestos ocultos (móvil): siempre hay un botón visible.
- Todo lo que comunica estado lleva **color + icono + texto**.
- Modales con foco atrapado y retorno del foco al cerrar; anuncios `aria-live` para SOS nuevos y en MAUI `SemanticScreenReader.Announce`.
- Objetivos táctiles ≥ 48 dp en la app; ≥ 44 px en web para controles principales (los secundarios densos de escritorio pueden ser de 32 px con separación).
