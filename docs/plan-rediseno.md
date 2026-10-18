# Plan de rediseño de la interfaz — Sistema Integrado CAD

Rediseño visual y de experiencia del **panel web** (operador y administrador) y de la **app móvil** (ciudadano y sereno). Dirección: **moderna pero formal, en tonos azules, con animaciones con propósito y una interfaz que cada rol entienda a la primera**.

Base de partida: las 84 capturas web y 67 móviles de [capturas/](capturas/README.md) (rama `sprint5/cierre-sprint5`, 17–18/10/2026).

## Avance

| Fase | Estado | Entregables |
|---|---|---|
| 0 · Auditoría | ✅ 18/10/2026 | [Crítica](rediseno/fase0-critica.md) · [Decisiones de UX por rol](rediseno/fase0-decisiones-ux.md) · [PRODUCT.md](../PRODUCT.md) |
| 1 · Sistema de diseño | ✅ 18/10/2026 | `tokens.css` v2 · `Colors.xaml` y `Styles.xaml` v2 · Public Sans en web y app · [Textos](rediseno/fase1-textos.md) · guía «Sistema CAD v2» publicada como página |
| Revisión visual (adelantada de la fase 5) | ✅ 18/10/2026 | [tests/Cad.Visual](../tests/Cad.Visual/README.md): Playwright + axe-core sobre la base desechable `cad_visual`; 18 pantallas × 3 anchos con referencia aprobada; deudas conocidas en `pruebas/deudas.js` |
| 2 · Dirección y prototipos | ✅ 18/10/2026 | Ronda de dirección de la central: elegida «Mapa protagonista» (contrato en `.impeccable/surfaces/`) · [prototipos navegables](rediseno/prototipos/index.html) de ingreso, central (tres cargas), detalle de SOS, casos y la app (ciudadano, acompañamiento, sereno), también publicados como página privada |
| 3 · Implementación web | En curso | ✅ Barra superior en todo el panel (menú desplegable por debajo de 1100 px) · ✅ Central «Mapa protagonista» (`central.css`): mapa a pantalla completa, cola y serenos flotantes y plegables, línea de despacho al sereno más cercano, diálogo modal de asignación, capas en la leyenda, SOS nuevo con pulso y reordenación en el sitio · Pendiente: detalle de SOS, reportes, casos, acompañamientos, mapa de concentración, administración, quitar los rótulos sobre el título y agrupar marcadores superpuestos |

Ajustes respecto al plan inicial, decididos en las fases 0 y 1:

- **Capa de acompañamiento del mapa** en `#0D9488` (`--mapa-acompanamiento`): con `#0F766E` no pasaba el mínimo de croma de la validación de daltonismo. El texto sigue en `#0F766E`.
- **Serenos en el mapa** en `--mapa-sereno` (marino) con forma de escudo: el azul de acción y el violeta de reportes eran indistinguibles para deuteranopía (ΔE 0.3).
- **Se elimina la etiqueta sobre el título** («OPERADOR», «CIUDADANO»): el rol pasa a la navegación. El estilo `Sobretitulo` queda marcado como obsoleto hasta la fase 4.
- **«Recibido» pasa de verde a azul informativo**: es un estado inicial, no un logro. Se añade el tono `progreso` para Asignada, En camino y En atención.
- **Fase 2 con `impeccable`**: la ronda de dirección (`concept-seed`) presentó tres organizaciones de la central y se eligió **«Mapa protagonista»**. Esto ajusta dos decisiones de la fase 0:
  - **D1:** la navegación del panel pasa a una **barra superior** marino con secciones e indicadores en vivo (menú desplegable por debajo de 1100 px), en lugar de la barra lateral.
  - **D2:** la central deja de tener tres columnas fijas: el **mapa ocupa toda la pantalla** y la cola (izquierda) y los serenos (derecha) flotan encima como paneles plegables; por debajo de 760 px se apilan bajo el mapa.
  - El movimiento distintivo es la **línea de despacho**: al elegir un caso, une el marcador con el sereno disponible más cercano y muestra la distancia.
- **Cierre de la dirección**: el contrato de `impeccable` exige, al terminar la implementación (fase 3), la revisión final del *finish reviewer* y el `DESIGN.md` generado a partir de lo construido.

---

## 1. Diagnóstico de la interfaz actual

### Lo que ya está bien y se conserva

- Tokens de diseño centralizados ([tokens.css](../src/Cad.Server/wwwroot/css/tokens.css) y [Colors.xaml](../src/Cad.Movil/Resources/Styles/Colors.xaml)) con contraste WCAG 2.2 verificado y los mismos nombres en web y móvil.
- Semántica de color clara: SOS en rojo (siempre con icono y texto), reportes en violeta y acompañamientos en verde.
- Textos claros y honestos, y avisos de que es un «MVP académico».
- Objetivos táctiles de 44 px y foco visible.

### Problemas que resuelve el rediseño

| # | Problema | Dónde se ve | Efecto en el usuario |
|---|---|---|---|
| P1 | **Aspecto plano y genérico**: tarjetas blancas iguales, sin jerarquía de profundidad ni identidad institucional | Todas las pantallas | Parece un formulario, no un centro de monitoreo |
| P2 | **Exceso de texto explicativo** en cada tarjeta (p. ej. la leyenda del mapa operativo, «Disponible: operativo y sin asignación…») | `031`, `081`, `120` | El operador lee en vez de actuar; lo urgente compite con la ayuda |
| P3 | **La central no prioriza**: SOS, reportes y serenos se apilan en una columna larga y el mapa queda a la derecha sin protagonismo | `031`, `130` | En una emergencia hay que hacer scroll para ver serenos |
| P4 | **El detalle de un SOS no muestra el flujo**: 6 tarjetas sueltas (ciudadano, ubicación, clasificación, asignación, cierre, tiempos) sin indicar el paso actual ni la siguiente acción | `032`, `040` | El operador no sabe qué le toca hacer ahora |
| P5 | **Estados solo como texto** en tablas («Pendiente», «Derivada», «Cerrada») | `050`, `110` | Escaneo lento; la tabla de casos tiene 10 columnas y celdas de 4 líneas |
| P6 | **Navegación superior saturada** (5 enlaces + usuario + rol + salir); en 390 px ocupa media pantalla | `031`, `130` | En pantallas medianas se rompe el encabezado |
| P7 | **App móvil sin navegación**: no hay barra inferior; «Mis casos» y «Notificaciones» se alcanzan bajando en el inicio | `009`, `010`, `027` | El ciudadano no encuentra sus casos |
| P8 | **Ingreso móvil con detalles técnicos** visibles (dirección del servidor, «Diagnóstico de ubicación (SPIKE01)») | `001` | Confunde al ciudadano |
| P9 | **El sereno ve un muro de texto** en el despacho (identificador UUID, coordenadas en crudo) y la acción principal no destaca frente al resto | `054`, `056` | En la calle necesita ver «a dónde» y «qué hago» de un vistazo |
| P10 | **Sin movimiento**: llegan SOS nuevos, cambian estados y se envían formularios sin transición ni confirmación visual | Todas | Los cambios en tiempo real pasan desapercibidos |
| P11 | Botones de «Ver avances» a ancho completo en cada tarjeta de «Mis casos» | `027` | Ruido; toda la tarjeta debería ser pulsable |

---

## 2. Dirección visual

### Personalidad

**Institucional, sereno y preciso.** Inspiración: paneles de control de servicios públicos y aplicaciones bancarias modernas. Nada de degradados llamativos ni glassmorphism; profundidad mediante capas de azul marino, sombras suaves y una sola nota de acento celeste.

### Paleta (verificada en la fase 1; valores completos en `tokens.css`)

| Token | Valor | Uso |
|---|---|---|
| `--color-marino` | `#0B1F44` | Barra lateral, encabezados, fondo del ingreso |
| `--color-marino-elevado` | `#132C5C` | Hover sobre marino, superficies elevadas oscuras |
| `--color-primario` | `#1D4ED8` | Botón principal, enlaces (blanco encima ≈ 6.7:1) |
| `--color-primario-hover` | `#1A3FB0` | Hover (presionado: `#16348F`) |
| `--color-acento` | `#5CC8FF` | Indicador de pestaña activa, foco sobre marino, pulsos del mapa (solo decorativo o sobre fondo oscuro) |
| `--color-tenue` | `#E6EEFD` | Fondos de selección, chips informativos |
| `--color-fondo` | `#F3F6FB` | Fondo de página |
| `--color-superficie` | `#FFFFFF` | Tarjetas |
| `--color-texto` | `#0D1B33` | Texto principal |
| `--color-texto-secundario` | `#475A75` | Texto de apoyo |
| `--color-urgente` | `#B42318` | **SOS: se mantiene rojo**, nunca azul |
| `--color-reporte` | `#6D28D9` | Reportes (se mantiene) |
| `--color-acompanamiento` | `#0F766E` | Acompañamientos: verde azulado, más coherente con la paleta fría |

Los estados (éxito, advertencia, error, información) se mantienen con sus valores verificados. **Regla:** el azul es la marca; el color de estado solo comunica estado y siempre va con icono y texto.

### Tipografía

- **Public Sans** (tipografía abierta de uso gubernamental, formal y legible) autoalojada en `wwwroot/fonts` y en `Resources/Fonts` de MAUI: sin dependencia de CDN.
- Cifras tabulares (`font-variant-numeric: tabular-nums`) en horas, cronómetros, tiempos de respuesta y coordenadas.
- Escala: 13 / 14 / 16 / 18 / 22 / 28 / 36 px; los títulos de página pasan a 28 px con peso 700 y tracking ligeramente negativo.

### Formas, profundidad e iconografía

- Radios: 8 px controles, 12 px tarjetas, 999 px chips de estado.
- Tres niveles de sombra (`--sombra-1/2/3`) en lugar de solo bordes.
- Iconos: un único set lineal (Lucide, en SVG en línea a través del parcial `_Icono.cshtml` ya existente; en MAUI como `FontImageSource` o SVG convertidos).
- **Chips de estado** con icono + color + texto para todos los estados de caso: Pendiente, Verificado, Clasificado, Asignado, En camino, En atención, Cerrado, Cancelado, Recibido, En revisión, Admitido, Derivado, Descartado.

### Principios de movimiento

| Principio | Regla |
|---|---|
| Propósito | Se anima solo para **orientar** (de dónde viene algo), **confirmar** (se guardó) o **alertar** (llegó un SOS) |
| Duración | 150–250 ms en interacciones; 300–400 ms en entradas de página; nunca más de 500 ms |
| Curva | `cubic-bezier(0.2, 0, 0, 1)` para entradas; `ease-out` para salidas |
| Propiedades | Solo `transform` y `opacity` (rendimiento) |
| Accesibilidad | `prefers-reduced-motion` desactiva todo salvo cambios de opacidad; en MAUI se respeta la preferencia del sistema |
| Urgencia | El SOS nuevo es la **única** animación persistente (pulso del marcador y del indicador), y se detiene al abrir el caso |

---

## 3. Experiencia por rol

### Operador de central (web, escritorio 1366–1920 px)

**Objetivo:** ver lo urgente sin hacer scroll y saber siempre cuál es la siguiente acción.

- **Navegación lateral** colapsable (Central, Reportes, Casos, Acompañamientos, Mapa de concentración) con contadores en vivo; el usuario, el rol y «Salir» pasan al pie de la barra. Libera el encabezado (P6).
- **Central en tres zonas** (P3):
  - Franja superior de indicadores: SOS pendientes, reportes admitidos, acompañamientos activos, serenos disponibles/operativos, estado de conexión en tiempo real.
  - Izquierda: **cola de trabajo** con pestañas *SOS · Reportes · Acompañamientos*; los SOS siempre arriba y con borde rojo.
  - Centro: **mapa protagonista** con leyenda compacta e interactiva (chips para mostrar/ocultar capas) en lugar del párrafo explicativo (P2).
  - Derecha: **serenos** con avatar de iniciales, chip de disponibilidad y antigüedad de la última posición.
- **Llegada de un SOS:** la tarjeta entra deslizándose con un destello rojo suave, el marcador del mapa pulsa, el contador se incrementa con animación y suena el aviso sonoro ya existente (P10).
- **Detalle de SOS/reporte** (P4): encabezado con **línea de progreso del caso** (Pendiente → Verificado → Clasificado → Asignado → En camino → En atención → Cerrado) y una **tarjeta de «Siguiente acción»** destacada; el resto de los datos se agrupa en una columna de contexto (ciudadano, ubicación con minimapa, tiempos). Las acciones no disponibles se explican en una línea, no en una tarjeta entera.
- **Tablas** (Reportes, Casos, Historial) (P5): chips de estado, fila completa pulsable, encabezado fijo, columnas de tiempos agrupadas en una sola columna «Tiempos» con tooltip, filtros en una barra horizontal plegable con chips de filtros activos.
- **Textos de ayuda** largos → icono «i» con tooltip o bloque plegable «¿Cómo funciona?».
- **Modo noche (opcional, fase 6):** variante oscura de la central para turnos nocturnos, reutilizando los tokens.

### Administrador (web)

**Objetivo:** gestionar cuentas sin errores.

- Inicio con tarjetas resumen (operadores, serenos, cuentas activas/desactivadas) y acceso directo a «Nueva cuenta».
- Tabla de cuentas con iniciales, chip de rol, chip de estado, buscador y filtro por rol/estado.
- Formularios en una sola columna con validación en línea; confirmaciones (desactivar) en **diálogo modal** con la consecuencia explicada («Sara tiene un despacho vigente: pasará a la central»).
- Notificación tipo *toast* tras crear, modificar o desactivar.

### Ciudadano (app móvil)

**Objetivo:** pedir ayuda en un toque y entender qué pasa con su caso.

- **Barra de navegación inferior** (P7): *Inicio · Mis casos · Avisos · Cuenta* (con insignia de no leídos en Avisos). El ingreso, el SOS en curso y el formulario de reporte quedan fuera de las pestañas.
- **Inicio:** encabezado marino con saludo y estado de ubicación como chip; **botón SOS** grande con anillo que respira suavemente (sin parpadeo), retroalimentación háptica y escala al pulsar; debajo, dos mosaicos con icono: «Reportar una incidencia» y «Acompañamiento».
- **Tras emitir un SOS:** pantalla de confirmación con check animado, hora y estado, y acceso directo a «Ver mi caso» o «Cancelar SOS».
- **Mis casos** (P11): tarjetas pulsables con chip de estado, tipo e icono por origen; *pull-to-refresh*; estado vacío ilustrado.
- **Detalle del caso:** **línea de tiempo vertical** de avances, con la evaluación (estrellas) al cerrarse.
- **Acompañamiento:** cronómetro dentro de un **anillo de progreso circular**, indicador «enviando ubicación» con pulso sutil, y la confirmación de bienestar como hoja inferior con dos botones grandes («Estoy bien» / «Necesito ayuda»).
- **Reporte:** formulario por secciones (Qué · Cuándo · Dónde) con selector segmentado para la forma de indicar el lugar en lugar de tres botones apilados.
- **Ingreso** (P8): logotipo, formulario y «Crear cuenta»; la dirección del servidor y el diagnóstico pasan a un bloque plegable «Opciones de desarrollo», visible solo en compilaciones de depuración.

### Sereno (app móvil)

**Objetivo:** en la calle, saber en dos segundos si está operativo, a dónde ir y qué pulsar.

- **Interruptor de estado operativo** grande en el encabezado (verde azulado = operativo, gris = no operativo) con transición animada y texto de estado.
- **Despacho como tarjeta protagonista** (P9): tipo y estado con chip, dirección/referencia arriba, minimapa o botón «Abrir en mapas», información de la central en un bloque plegable y el identificador del caso oculto tras «Detalles».
- **Botón de acción progresivo** fijo en la parte inferior que cambia según el estado: *Voy en camino → Inicié la atención → Cerrar el caso*, con transición entre etiquetas.
- Formulario de cierre en pasos cortos (resultado, tipificación, notas) con validación en línea.
- Pantallas de sesión terminada o cuenta desactivada con un mensaje claro y una única acción.

---

## 4. Restricciones técnicas (no negociables)

1. **Se mantiene el stack**: Razor Pages + CSS propio + JS sin framework + Leaflet + SignalR en web; .NET MAUI XAML en móvil. El rediseño es de presentación, no de lógica.
2. **No se tocan los atributos `data-*`, los `id` ni los textos que verifican las pruebas**: hay ~180 aserciones sobre el HTML en `tests/Cad.Server.Tests` (p. ej. `data-sereno`, `data-disponible`, `data-lat`, `data-caso-mapa`, `data-aviso`, `data-medicion`) y los scripts de `wwwroot/js` dependen de ellos. Cada fase termina con `dotnet test Cad.slnx` en verde.
3. **Accesibilidad WCAG 2.2 AA**: cada token nuevo se verifica (texto ≥ 4.5:1; bordes y foco ≥ 3:1) y se documenta junto al valor, como hoy.
4. **El SOS sigue siendo rojo** y se identifica siempre con icono + texto «SOS».
5. **Sin dependencias de CDN**: fuentes e iconos autoalojados.
6. **Se conserva el aviso** de «MVP académico · datos simulados · no es un canal oficial de emergencias».
7. Diseño adaptable verificado a 390, 768, 1366 y 1920 px.

---

## 5. Fases, entregables y skills

Cada fase indica **qué skill usar y en qué momento**. Las fases 3 y 4 pueden ir en paralelo (web y móvil no comparten archivos más allá de los tokens).

### Fase 0 — Auditoría y criterios (0,5 día)

| Paso | Skill | Para qué |
|---|---|---|
| Crítica heurística de las capturas web y móviles | `design:design-critique` | Confirmar y priorizar la lista P1–P11 |
| Principios de jerarquía y carga cognitiva por rol | `ux-first-principles` | Decidir qué se muestra primero en cada pantalla |
| Reglas específicas de escritorio para la central | `ux-desktop` | Densidad, atajos de teclado, hover, multi-ventana |
| Reglas específicas de móvil para ciudadano y sereno | `ux-mobile` | Alcance del pulgar, objetivos táctiles, tareas de un solo foco |

**Entregable:** este documento ajustado y priorizado.

### Fase 1 — Sistema de diseño (1–2 días)

| Paso | Skill | Para qué |
|---|---|---|
| Tokens (color, tipografía, espaciado, sombras, radios, movimiento) | `design:design-system` | Definir la fuente única de tokens web y su espejo XAML |
| Paleta, tipografía y estados | `impeccable` (modos *colorize*, *typography*) | Ajustar la paleta azul y verificar contrastes |
| Leyendas del mapa, indicadores y mapa de concentración | `dataviz` | Colores de capas y tarjetas de indicadores coherentes y accesibles |
| Textos de chips, botones y mensajes vacíos | `design:ux-copy` | Reducir el texto explicativo sin perder precisión (P2) |

**Entregables:** `tokens.css` v2, `Colors.xaml`/`Styles.xaml` v2, catálogo de componentes (botón, campo, chip de estado, tarjeta, tabla, *toast*, modal, línea de progreso, línea de tiempo) y una **guía de estilo visual** publicada como página para revisarla con el equipo.

### Fase 2 — Prototipos de las pantallas clave (1–2 días)

Pantallas: Ingreso web, Central, Detalle de SOS, Casos, Inicio ciudadano, Acompañamiento, Despacho del sereno.

| Paso | Skill | Para qué |
|---|---|---|
| Ronda de dirección: 2–3 propuestas de composición y «movimiento distintivo» de la central dentro de la paleta fijada | `impeccable` (`shape` + `concept-seed --mode operate`) | Elegir la dirección en una página de decisión antes de prototipar |
| Diseñar las pantallas sobre los tokens | `impeccable` o `frontend-design:frontend-design` | Prototipos HTML navegables de alta fidelidad |
| (Opcional) llevarlas a Figma | `figma:figma-generate-design` | Solo si se quiere revisar en Figma; requiere autorizar el conector de Figma |

**Entregable:** prototipos aprobados antes de tocar el código del proyecto.

### Fase 3 — Implementación web (4–5 días)

Orden por impacto: (1) layout y navegación lateral → (2) Ingreso y Acceso denegado → (3) Central → (4) Detalle de SOS y de reporte → (5) Reportes, Casos e Historial → (6) Acompañamientos y Avisos → (7) Mapa de concentración → (8) Administración de cuentas.

| Paso | Skill | Para qué |
|---|---|---|
| Implementación de layout y componentes | `impeccable` | Rediseño de las vistas Razor y `panel.css` |
| Animaciones (llegada de SOS, contadores, *toasts*, transiciones entre páginas con View Transitions API, esqueletos de carga) | `animate` | Decidir curva, duración, interrupción y salida de cada una |
| Pulido de microinteracciones (hover, presionado, foco, tablas) | `emil-design-eng` | El detalle que hace que la interfaz se sienta cuidada |

**Cierre de fase:** `dotnet test Cad.slnx` en verde y revisión manual a 1366 y 390 px.

### Fase 4 — Implementación móvil (4–5 días)

Orden: (1) tema y estilos globales → (2) navegación inferior (Shell con `TabBar`) → (3) Ingreso y Registro → (4) Inicio ciudadano y SOS → (5) Mis casos, Mi caso y Avisos → (6) Reporte → (7) Acompañamiento y bienestar → (8) Sereno.

| Paso | Skill | Para qué |
|---|---|---|
| Tema, tokens y (opcional) modo oscuro | `maui-theming` | `ResourceDictionary` v2 y `AppThemeBinding` |
| Icono de la app y pantalla de inicio en azul marino | `maui-app-icons-splash` | Identidad desde el primer segundo |
| Bordes de pantalla y teclado | `maui-safe-area` | Encabezado marino de borde a borde sin solaparse |
| Listas de casos y avisos | `maui-collectionview` | Tarjetas pulsables, *pull-to-refresh*, estados vacíos |
| SOS, anillo del cronómetro, botón progresivo del sereno, transiciones | `maui-animations` (+ `maui-graphics-drawing` para el anillo) | Movimiento nativo y fluido |
| Gestos (hoja inferior de bienestar, deslizar) | `maui-gestures` | Interacciones táctiles naturales |

**Cierre de fase:** pruebas de `tests/Cad.Movil.Tests` en verde y recorrido completo en el emulador.

### Fase 5 — Calidad y accesibilidad (1–2 días)

| Paso | Skill | Para qué |
|---|---|---|
| Revisión del HTML frente a las guías de interfaz web | `web-design-guidelines` | Semántica, foco, formularios, rendimiento |
| Auditoría de accesibilidad web | `design:accessibility-review` | WCAG 2.2 AA: contraste, teclado, lectores de pantalla |
| Accesibilidad móvil | `maui-accessibility` | `SemanticProperties`, TalkBack, anuncios del SOS |
| Auditoría final de coherencia y pulido | `impeccable` (modos *audit* y *polish*) | Detectar incoherencias entre pantallas |
| Arrancar la app y tomar las capturas «después» | `run` + navegador | Rehacer el catálogo de `docs/capturas` para comparar antes/después |

### Fase 6 — Documentación y extras (1 día)

| Paso | Skill o agente | Para qué |
|---|---|---|
| Documento del sistema de diseño final (`DESIGN.md`) | agente `impeccable-documenter` | Derivar la documentación de lo realmente implementado |
| Especificación para el equipo | `design:design-handoff` | Medidas, estados y comportamiento de cada componente |
| (Opcional) Modo noche de la central | `impeccable` + `animate` | Variante oscura para turnos nocturnos |

---

## 6. Criterios de aceptación

- [ ] Ninguna prueba existente falla (`dotnet test Cad.slnx` y `CadMovil.slnx`).
- [ ] Todos los tokens nuevos tienen su contraste documentado y cumplen WCAG 2.2 AA.
- [ ] En la central a 1366 × 768, el operador ve sin scroll: SOS pendientes, mapa y serenos disponibles.
- [ ] En el detalle de un caso, la siguiente acción es identificable en menos de 3 s (prueba con 3 compañeros).
- [ ] El ciudadano llega a «Mis casos» en un toque desde cualquier pestaña.
- [ ] El sereno ve el estado operativo y la acción del despacho sin hacer scroll.
- [ ] Todas las animaciones respetan `prefers-reduced-motion` / la preferencia del sistema.
- [ ] Catálogo de capturas «después» completo y comparable con el «antes».

## 7. Riesgos

| Riesgo | Mitigación |
|---|---|
| Romper pruebas que leen el HTML | No cambiar `data-*`, `id` ni textos asertados; ejecutar las pruebas al cerrar cada vista |
| Animaciones que distraen en una central de emergencias | Solo el SOS nuevo tiene movimiento persistente; el resto, transiciones cortas |
| El azul se confunde con estados | Azul reservado para marca y acciones; estados con su color + icono + texto |
| Alcance excesivo para el calendario académico | Fases 3 y 4 priorizadas por impacto; fase 6 opcional |
| El cambio de la navegación móvil (Shell con pestañas) afecta a la navegación existente | Hacerlo al inicio de la fase 4 y probar todos los flujos de `docs/demo-sprint*.md` |

## 8. Organización del trabajo

- Rama base: `sprint5/cierre-sprint5` (contiene todas las funciones). Ramas: `rediseno/sistema-diseno`, `rediseno/panel-web` y `rediseno/app-movil`, una PR por fase.
- Antes de empezar: confirmar en git el borrado de las capturas antiguas (`docs/capturas/demo-sprint*`, `ingreso/`, etc.), que hoy está sin confirmar.
- Estimación total: **12–17 días de trabajo**, que encaja como un Sprint 6 de rediseño.
