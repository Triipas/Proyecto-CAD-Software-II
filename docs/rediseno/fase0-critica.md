# Fase 0 · Crítica de diseño de la interfaz actual

Crítica estructurada (skill `design:design-critique`) sobre el catálogo de [capturas](../capturas/README.md) del 17–18/10/2026. Etapa: **producto funcional, previo al rediseño visual**. Se citan las capturas por su número (`web/031`, `movil/054`).

## Impresión general

La interfaz es **correcta, accesible y honesta**, pero se ve como un formulario administrativo y no como un centro de monitoreo: todo tiene el mismo peso visual (tarjeta blanca + título + párrafo), así que lo urgente no se impone por diseño, solo por color. La mayor oportunidad es **jerarquía**: decidir por cada rol qué se ve primero y qué se esconde hasta que hace falta.

## Usabilidad

| Hallazgo | Severidad | Recomendación |
|---|---|---|
| En la central (`web/031`) los serenos quedan bajo el pliegue a 1366 × 768; para asignar un SOS hay que bajar y luego volver | 🔴 Crítica | Distribución en tres zonas (cola · mapa · serenos) visibles a la vez |
| La confirmación de asignación aparece arriba de la página (`web/038`), lejos del marcador o la tarjeta que la originó | 🔴 Crítica | Diálogo modal centrado o panel lateral junto al caso, con el foco atrapado |
| El detalle del SOS (`web/032`) presenta 6 tarjetas sin indicar el paso actual; la acción principal («Registrar intento») está a la derecha y la clasificación a la izquierda | 🔴 Crítica | Línea de progreso del caso + tarjeta «Siguiente acción» única |
| El ciudadano no tiene navegación: «Mis casos» y «Notificaciones» están al fondo del inicio (`movil/009`, `010`) | 🔴 Crítica | Barra inferior con 4 pestañas |
| El despacho del sereno (`movil/054`, `057`) mezcla mensajes, horas, UUID, coordenadas y formulario de cierre en una sola tarjeta; el botón principal queda al final | 🟡 Moderada | Tarjeta de despacho resumida + botón de acción fijo abajo; detalles plegables |
| La ventana emergente del mapa muestra el identificador UUID (`web/038`) | 🟡 Moderada | Mostrar tipo, estado, ciudadano, hora y un botón «Abrir caso» |
| La tabla de casos (`web/110`) tiene 10 columnas y celdas de hasta 4 líneas | 🟡 Moderada | Chips de estado, columna «Tiempos» única, fila pulsable, ciudadano + sereno en una celda |
| Los enlaces de acción en las tarjetas de SOS («Verificar y registrar contacto», «Elegir sereno para asignar») parecen texto subrayado y no botones | 🟡 Moderada | Botón primario + secundario compactos |
| El ingreso móvil muestra la dirección del servidor y «Diagnóstico de ubicación (SPIKE01)» (`movil/001`) | 🟡 Moderada | Bloque plegable «Opciones de desarrollo», solo en compilación de depuración |
| «Ver avances» como botón a ancho completo en cada tarjeta (`movil/027`) | 🟢 Menor | Tarjeta completa pulsable con chevron |
| El botón «Salir» del sereno parece deshabilitado (borde y texto grises, `movil/053`) | 🟢 Menor | Estilo terciario claro o ícono en el encabezado |

## Jerarquía visual

- **Qué atrae primero la mirada:** en la central, el mapa (por su tamaño y color) y el título; el SOS solo destaca por el bloque rosado. En el inicio del ciudadano, el botón SOS — **correcto**. En el despacho del sereno, el aviso verde «Tienes un nuevo despacho» — **incorrecto**: debería ser el destino y la acción.
- **Flujo de lectura:** de arriba abajo en una columna, con párrafos explicativos antes de cada lista (`web/031`, `web/081`, `web/120`). El operador lee ayuda que ya conoce antes de llegar al dato.
- **Énfasis:** títulos de tarjeta, etiquetas de campo y valores tienen pesos parecidos (600–700). Los valores clave (hora del SOS, precisión, tiempo restante) no destacan, salvo el cronómetro del acompañamiento (`movil/039`), que sí funciona.

## Consistencia

| Elemento | Problema | Recomendación |
|---|---|---|
| Campos en MAUI | Picker, Entry y Editor muestran **la línea inferior nativa de Android dentro del borde** (`movil/020`, `057`): doble borde | Quitar el subrayado con un *handler* y usar un único contenedor con borde |
| Estados | En web se mezclan formatos: chip con icono («Recibido» verde, `web/050`), texto plano («Pendiente», `web/110`) y «SOS · Recibido» en título (`movil/027`) | Componente único de chip de estado, igual en web y móvil |
| Color de «Recibido» | Se muestra en verde de éxito (`web/050`) aunque es un estado inicial, no un logro | Estados iniciales en azul informativo; verde solo para «Cerrado»/«Atendido» |
| Mensajes de confirmación | Usan la misma caja verde que los avisos persistentes («Estás operativo», «Ubicación disponible») | Separar *toast* temporal (confirmación) de *banner* de estado (persistente) |
| Encabezados de página | La etiqueta del rol encima del título («OPERADOR», «CIUDADANO») se repite en todas las pantallas sin aportar | Mover el rol a la navegación o al perfil; dejar el título solo |
| Botones secundarios | Mismo estilo para acciones muy distintas («Salir», «Reportar una incidencia», «Acompañamiento») | Mosaicos con icono para entradas de flujo; botón terciario para salir |

## Accesibilidad

- **Contraste de color:** ✅ pasa. Los tokens actuales están verificados (texto ≥ 4.5:1, bordes ≥ 3:1). Hay que mantener ese estándar en la paleta nueva.
- **Objetivos táctiles:** ✅ 44 px en web y ≥ 48 dp en la app. Los enlaces en línea de las tarjetas de SOS (`web/031`) son más pequeños: pasarlos a botones.
- **Legibilidad:** ✅ 16 px base. Textos de ayuda de 13 px muy largos (`web/032`, `web/081`) cansan: acortarlos.
- **No depender solo del color:** ✅ el SOS siempre lleva icono + texto. ⚠️ En el mapa, «relleno vs. borde» como única diferencia entre estados (`web/031`) es difícil de percibir; añadir icono o forma.
- **Movimiento:** hoy no hay animaciones; al añadirlas, respetar `prefers-reduced-motion`.

## Lo que funciona bien

- El botón SOS del ciudadano es inequívoco y está donde debe.
- El cronómetro del acompañamiento (`movil/039`) tiene una jerarquía tipográfica excelente; es el modelo a seguir.
- Los mensajes de error y validación son precisos y no revelan información sensible (`web/002`).
- La separación cromática SOS / reporte / acompañamiento es coherente en mapa y listas.
- La versión angosta del panel (`web/130`–`133`) es usable aunque larga.

## Recomendaciones prioritarias

1. **Reorganizar la central en tres zonas y llevar la asignación a un diálogo** — es la pantalla donde un retraso cuesta más; todo lo necesario para despachar debe verse sin scroll.
2. **«Siguiente acción» + línea de progreso en el detalle de caso (web) y en el despacho (sereno)** — el usuario no debe deducir el flujo leyendo seis tarjetas.
3. **Navegación inferior en la app del ciudadano** — hoy sus casos y avisos son prácticamente invisibles.
4. **Sistema de chips de estado y componentes unificados web/MAUI** — resuelve la mayoría de inconsistencias de una vez (incluido el doble borde de los campos MAUI).
5. **Reducir el texto explicativo** a tooltips o bloques plegables — baja la carga cognitiva sin perder la precisión del dominio.
