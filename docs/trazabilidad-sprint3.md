# Trazabilidad del Sprint 3

Sprint 3: reportes, revisión, historial y seguimiento ciudadano.
IDs de tarea y resultados verificables tomados del Plan Scrum (apartado 3.2). Estados según el Plan Scrum 3.3: *Pendiente*, *En curso*, *Implementada*, *Integrada*, *Aceptada*.

> **Calendario:** el del Plan Scrum (apartado 4.5). Las evidencias de este documento llevan la fecha en que se registraron.

**Objetivo del sprint (Plan Scrum):** incorporar el flujo de reportes (registro, búsqueda, revisión, admisión, derivación y descarte), verificar la asignación de reportes admitidos (HU20-CA07) y ofrecer historial de casos y seguimiento al ciudadano.

**Historias seleccionadas (33 SP):** HU10 (5), HU26 (5), HU14 (3), HU15 (2), HU16 (3), HU17 (2), HU27 (5), HU30 (3), HU31 (5) y la verificación de HU20-CA07 (sin SP propios).

**Condiciones de aceptación (Plan Scrum):** cada HU con todos sus CA verificados; HU17 y HU27 no se acreditan mientras tengan cláusulas pendientes (HU17-CA04 con HU29; HU27-CA02 con HU09), que siguen abiertas hasta el Sprint 5; un reporte admitido se asigna con las mismas reglas que un SOS (HU20-CA07) y con ello se acepta HU20; HU03 no se acredita.

## Estado de las tareas

| ID | HU | Tarea | Estado | Evidencia |
|---|---|---|---|---|
| S3-T01 | HU10 | Registro de reporte con ubicación del hecho por mapa o por texto y categoría del catálogo compartido | Implementada | Servidor: `POST /api/reportes` (solo ciudadano): caso «Reporte» en estado «Recibido» con su detalle (tipo, descripción, fecha y hora aproximadas, ubicación del hecho por punto o por referencia) y su evento, en una transacción; `GET /api/reportes/categorias`; `ReporteTests`. App: formulario «Reportar una incidencia» con la advertencia de SOS, el catálogo de tipos y el mapa Leaflet; `RegistradorReporte`, `ReporteVistaModelo` y `Cad.Movil.Tests/ReporteTests`; verificado en el emulador. |
| S3-T02 | HU10 | Posición del dispositivo solo por elección del ciudadano; idempotencia del registro | Implementada | Servidor: la ubicación del hecho se guarda aparte de la ubicación del dispositivo, con su fuente; una referencia con coordenadas se rechaza (también la base de datos); idempotencia con el mecanismo de S1-T14. App: la posición solo se lee al pulsar «Usar mi posición actual» (y el permiso solo se pide entonces); los reintentos usan el mismo `IdSolicitud` y, sin confirmar, solo se ofrece reintentar el mismo reporte; verificado en el emulador con el servidor cortado. |
| S3-T03 | HU26 | Listado de casos con cinco filtros combinables | Pendiente | DP17 resuelta. |
| S3-T04 | HU26 | «Tipo por determinar» y casos sin coordenadas en el listado | Pendiente | «Tipo por determinar» ya existe (HU13). |
| S3-T05 | HU14 | Inicio de revisión y corrección de categoría conservando la original, con control de concurrencia | Implementada | Página «Reportes» (`/panel/reportes`) y detalle (`/panel/reportes/{id}`), solo operador: «Iniciar la revisión» (Recibido → En revisión, con responsable, fecha y hora) y «Corregir la categoría» (solo en revisión; original, anterior, nueva y responsable en `CorreccionesCategoria`); un solo responsable con revisiones simultáneas. `RevisionReporteTests`. |
| S3-T06 | HU15 | Admisión de un reporte para atención | Pendiente | DP21 resuelta. |
| S3-T07 | HU16 | Derivación con destino y acción, sin integración externa | Pendiente | DP21 resuelta. |
| S3-T08 | HU20 (CA07) | Asignación de un reporte admitido con las reglas de HU20 | Pendiente | La asignación del Sprint 2 ya vale para cualquier caso «Pendiente». |
| S3-T09 | HU17 | Descarte con justificación obligatoria | Pendiente | DP05 y DP21 resueltas. |
| S3-T10 | HU17 | HU17-CA04 registrado como pendiente hasta el Sprint 5 (mapa de HU29) | Pendiente | — |
| S3-T11 | HU27 | Historial consolidado de SOS y reportes, notas internas restringidas, sin acompañamiento | En curso (implementado y verificado para los SOS y para el registro, la revisión y la corrección de categoría de los reportes) | `/panel/casos/{id}/historial` (solo operador): emisión, intentos de contacto, clasificación, asignación y su fin, avances y cierre, en orden y con responsable y hora; resultado final identificado; notas internas rotuladas. `HistorialTests`. Las actuaciones de los reportes se añaden con HU14–HU17. |
| S3-T12 | HU27 | Vínculos con HU17 (CA03) y HU16 (CA04); «Cancelada» (CA02) pendiente hasta HU09 | Pendiente | — |
| S3-T13 | HU30 | «Mis casos» con control de titularidad y resultado público | Pendiente | DP05 resuelta. |
| S3-T14 | HU31 | Notificaciones persistentes de cambios públicos de los casos propios | Pendiente | DP05 y E4 resueltas. |
| S3-T15 | HU31 | Vista previa sin datos sensibles; acceso solo del titular | Pendiente | — |
| S3-T16 | HU03 | Verificación por rol de HU10–HU31 y registro acumulativo | Pendiente | — |
| S3-T17 | Transversal | Revisión de registros y ausencia de posiciones no elegidas por el ciudadano | Pendiente | — |
| S3-T18 | HU10–HU31 | Integración, despliegue y demostración del sprint | Pendiente | — |
| S3-T19 | RNF02 | Medición formal en el entorno de validación e informe, con revisión de sus registros (trasladada: S1-T18, S1-T20, S2-T15) | Pendiente | La ejecuta el equipo en Render ([pruebas.md](pruebas.md)). |
| S3-T20 | SPIKE01 | Repetición en un teléfono Android real y decisión sobre la nota técnica (trasladada: S1-T03, S1-T04) | Pendiente | Incluye la prueba de HU06, HU07, HU08 y HU18 en un dispositivo real. |
| S3-T21 | HU21 | Entrega real de la notificación push del despacho con la app cerrada (trasladada) | Pendiente | Credencial `Push__CredencialFcm` ya configurada en Render. |

## Decisiones del Product Owner (14/10/2026)

El PO aceptó las propuestas de la planificación. DP08, DP17 y DP21 aparecen en las historias sin enunciado en los documentos del proyecto: se registra la interpretación aceptada.

| Tema | Decisión | Efecto |
|---|---|---|
| E1 — Trabajo sin integrar | Commit y PR del cierre documental de los Sprints 1 y 2; el PR #19 (push en la app) se fusiona antes de desplegar el Sprint 3. | — |
| E2 — Fechas | Las fija el PO; sin fijar al 14/10/2026. | — |
| DP08 — datos del reporte (HU10) | La fecha y hora aproximadas del hecho son obligatorias y no pueden ser futuras; la descripción tiene un límite técnico de 2000 caracteres; sin adjuntos en el MVP. | S3-T01. |
| DP17 — búsqueda de casos (HU26, HU25) | El filtro de fechas se aplica a la fecha de registro; sin filtros, los casos más recientes primero, de 50 en 50. | S3-T03. |
| DP21 — admisión y derivación (HU15, HU16) | El motivo (admitir, derivar, descartar) es texto libre obligatorio; el destino de la derivación es texto libre, sin catálogo de entidades. | S3-T06, S3-T07, S3-T09. |
| DP05 — qué ve el ciudadano (HU17, HU30, HU31) | Estados públicos: «Recibido», «En revisión», «Admitido», «En atención» (agrupa Asignada, En camino y En atención), «Cerrado», «Derivado» y «Descartado». Son visibles el resultado público, el destino de la derivación y el motivo del descarte. Nunca: notas internas, intentos de contacto, operador ni sereno. Cada cambio de estado público genera una notificación. | S3-T09, S3-T13, S3-T14. |
| E3 — Mapa en la app (HU10-CA03) | Leaflet con OpenStreetMap dentro de la app (sin clave ni costo), como el mapa del panel. | S3-T01 (app). |
| E4 — Notificaciones al ciudadano (HU31) | Solo dentro de la app, en una bandeja persistente; la push al ciudadano queda para más adelante. | S3-T14, S3-T15. |
| E5 — Dónde ve el operador los reportes | Página «Reportes» del panel para recibidos y en revisión; los admitidos aparecen en la central junto a los SOS pendientes para asignarlos (HU15-CA02); búsqueda con filtros (HU26) en una página «Casos». | S3-T05, S3-T06, S3-T03. |
| E6 — Trasladados (S3-T19–S3-T21) | Los ejecuta el equipo con las guías existentes; el desarrollo registra los resultados. | — |

## Plan de bloques

Mismo método que en los sprints anteriores: cada bloque con pruebas automatizadas, pruebas de mutación y verificación de extremo a extremo; commit y PR solo con autorización.

| Bloque | Contenido | Tareas | Depende de |
|---|---|---|---|
| 1 | Historial de un caso (HU27) para los SOS, preparado para los reportes | S3-T11 (parte SOS) | — |
| 2 | Registro de reportes en el servidor (HU10) | S3-T01, S3-T02 (servidor) | DP08 |
| 3 | Reporte desde la app: formulario, mapa, posición por elección y advertencia de SOS (HU10) | S3-T01, S3-T02 (app) | E3 |
| 4 | Revisión y corrección de categoría (HU14) en la página «Reportes» | S3-T05 | E5 |
| 5 | Admitir, derivar y descartar (HU15–HU17) y asignación de un reporte admitido (HU20-CA07) | S3-T06–S3-T10, S3-T08, S3-T11, S3-T12 | DP21, DP05 |
| 6 | Búsqueda y filtros de casos (HU26) | S3-T03, S3-T04 | DP17 |
| 7 | «Mis casos» en la app (HU30) | S3-T13 | DP05 |
| 8 | Notificaciones de avances (HU31) | S3-T14, S3-T15 | DP05, E4 |
| 9 | Cierre del sprint: matriz por rol, registros, demostración | S3-T16–S3-T18 | — |

## Bloques ejecutados

| Bloque | Fecha | Tareas | Resultado |
|---|---|---|---|
| 1 — Historial de un caso (SOS) | 14/10/2026 | S3-T11 (parte SOS); S3-T16 (parcial) | `dotnet test`: 426/426 superadas (14 nuevas, incluida la ruta en la matriz). Release: 0 advertencias. 5 mutaciones detectadas: sin intentos de contacto, notas internas sin rótulo, sin orden temporal, sin resultado final y cualquier sesión con acceso. Chrome contra el servidor local, con el caso cerrado de la demostración del Sprint 2: 9 actuaciones en orden con su responsable, resultado final y notas internas rotuladas; 390 px sin desbordamiento; 0 errores de consola. Captura: [historial](capturas/panel/hu27-historial.png). |
| 2 — Registro de reportes (servidor) | 14/10/2026 | S3-T01, S3-T02 (servidor); S3-T11 (registro del reporte); S3-T16 (parcial) | `dotnet test`: 477/477 superadas (51 nuevas, incluidas las dos rutas en la matriz). Release: 0 advertencias. 14 mutaciones detectadas: sin el límite de fecha futura, referencia con coordenadas aceptada, sin la captura de la carrera de reintentos (detectada tras añadir una prueba determinista), límites de descripción y referencia, tipo fuera del catálogo, reiteración que devuelve un SOS, descripción en los registros, evento no visible para el titular, referencia vacía, clasificación duplicada en el historial, estado de la respuesta, ubicación ausente y fuente desconocida. De extremo a extremo contra el servidor local (cuenta ciudadana de prueba y Chrome): 201 «Recibido» y 200 con el mismo reporte en el reenvío; por mapa 201; fecha futura, referencia con coordenadas y envío incompleto rechazados con 400 y el motivo de cada campo; el reporte no aparece en la central de SOS; el historial muestra su registro, la ubicación del hecho y la fecha aproximada; 390 px sin desbordamiento; 0 errores de consola; el registro del servidor no contiene la descripción, la referencia ni las coordenadas. Captura: [historial de un reporte](capturas/panel/hu10-historial-reporte.png). |
| 3 — Reporte desde la app | 14/10/2026 | S3-T01, S3-T02 (app); S3-T16 (parcial) | `dotnet test` de la app: 165/165 superadas (40 nuevas); servidor sin cambios (477/477 en el bloque 2). Release de la app: 0 advertencias. 15 mutaciones: 14 detectadas y 1 equivalente (una referencia con coordenadas: «Escribir una referencia» ya borra el punto y ningún otro camino lo conserva). La del doble toque sobrevivía al principio y se detecta tras añadir una prueba con la lectura asíncrona de la sesión. Emulador Android 15 contra el servidor local, con la cuenta ciudadana de prueba: acceso desde el inicio; advertencia de SOS y «Ir al SOS»; catálogo de tipos del servidor; error de descripción vacía sin enviar nada; punto tocado en el mapa (Leaflet local y teselas de OpenStreetMap) guardado como «Mapa» (−12.058932, −77.046089), distinto de la posición del GPS del emulador (−12.046598, −77.043); «Usar mi posición actual» pide el permiso de Android solo al elegirlo y guarda «PosicionActual» (± 5 m); referencia guardada como «Texto» sin coordenadas; en los tres casos el caso queda sin ubicación del dispositivo. Sin servidor (proxy local cortado): «No se pudo confirmar… no se duplicará» y «Reintentar el mismo reporte» registra uno solo. Registros del servidor y del teléfono sin descripciones, referencias ni coordenadas. Capturas: [acceso](capturas/movil/30-reporte-acceso.png), [advertencia](capturas/movil/31-reporte-advertencia-sos.png), [campo obligatorio](capturas/movil/32-reporte-campo-obligatorio.png), [mapa](capturas/movil/33-reporte-punto-en-mapa.png), [permiso](capturas/movil/34-reporte-permiso-al-elegir.png), [posición actual](capturas/movil/35-reporte-posicion-actual.png), [referencia](capturas/movil/36-reporte-referencia.png), [sin confirmar](capturas/movil/37-reporte-no-confirmado.png), [reintentado](capturas/movil/38-reporte-reintentado.png). |
| 4 — Revisión y corrección de categoría | 15/10/2026 | S3-T05; S3-T11 (revisión y correcciones); S3-T16 (parcial) | `dotnet test`: 513/513 superadas (36 nuevas, incluidas las tres rutas en la matriz). Release: 0 advertencias. 14 mutaciones detectadas: sin comprobar «Recibido», sin comprobar el responsable previo, sin la nueva evaluación tras un conflicto, corrección fuera de la revisión, sin registrar la corrección, sin descartar la misma categoría, evento no público, reenvío del mismo operador tratado como error, bandeja sin los «En revisión», original mostrada como la vigente, corrección mal descrita en el historial, clasificación duplicada en el historial, corrección permitida en «Recibido» y página abierta a cualquier sesión. Chrome contra el servidor local (cuentas de prueba): dos reportes creados por la API de la app aparecen en «Reportes»; el detalle muestra el hecho, la ubicación del hecho con su mapa (marcador y teselas) o la referencia sin mapa; «Iniciar la revisión» deja al operador como responsable; la corrección muestra la original («Robo») y la vigente, y el historial registra el registro, la revisión y la corrección en orden; un administrador es enviado a «acceso denegado»; 390 px sin desbordamiento; 0 errores de consola. Capturas: [bandeja](capturas/panel/hu14-reportes.png), [detalle recibido](capturas/panel/hu14-detalle-recibido.png), [detalle corregido](capturas/panel/hu14-detalle-corregido.png), [historial](capturas/panel/hu14-historial.png). |

### Decisiones y observaciones del bloque 1

- **Orden del sprint:** el historial se empieza por los SOS porque no depende de ninguna decisión pendiente; la consulta está preparada para añadir las actuaciones de los reportes (revisión, corrección de categoría, admisión, derivación y descarte) en los bloques 4 y 5.
- **Fuente de las actuaciones:** se reúnen de los registros que ya existen (caso, intentos de contacto, clasificación, asignaciones, eventos de avance y cierre), sin duplicar datos. Cada actuación muestra su responsable y su marca de tiempo (CA01, CA03).
- **Resultado final (CA02):** un caso cerrado muestra arriba su estado, la fecha, la tipificación y el resultado público. «Cancelada», «Derivada» y «Descartada» se añaden con HU09, HU16 y HU17.
- **Notas internas (CA04):** solo las ve el personal autorizado y aparecen rotuladas. La página exige el rol de operador (CA06); un administrador, un sereno o un ciudadano no la obtienen, tampoco con una solicitud directa.
- **Acompañamiento (CA05):** el historial no incluye posiciones ni sesiones de acompañamiento (HU39).
- **Acceso:** enlace «Ver el historial del caso» en el detalle del SOS.

### Decisiones y observaciones del bloque 2

- **Modelo:** el reporte es un caso con origen «Reporte» y el nuevo estado «Recibido» (CA01). Sus datos propios están en `DetallesReporte` (uno por caso): descripción, fecha y hora aproximadas, ubicación del hecho y categoría original. Caso, detalle y evento `ReporteRegistrado` (visible para el titular) se guardan en una sola transacción: un rechazo no deja nada parcial (CA08).
- **Campos obligatorios (CA02, DP08):** tipo del catálogo compartido (DP07), descripción no vacía de hasta 2000 caracteres, fecha y hora aproximadas no futuras y ubicación del hecho. Un envío incompleto se rechaza con 400 y el motivo de cada campo, sin crear el caso. Sin adjuntos.
- **Margen del reloj:** «no futura» admite 5 minutos de diferencia entre el reloj del teléfono y el del servidor, para que «ahora» no se rechace en un teléfono algo adelantado. Es un margen técnico, no una regla de negocio; el PO lo aceptó (14/10/2026), igual que el rechazo de un identificador ya usado en un SOS y la clasificación inicial a nombre del ciudadano.
- **Ubicación del hecho (CA03, CA04, DP10):** con una fuente explícita: «Mapa» (punto elegido), «PosicionActual» (solo si el ciudadano eligió usar su posición) o «Texto» (referencia, obligatoria y sin coordenadas). Se guarda aparte de las columnas de ubicación del dispositivo del caso (HU08), que quedan vacías. Una referencia con coordenadas se rechaza: así una app defectuosa no puede adjuntar la posición del dispositivo sin elección del ciudadano. La base de datos lo garantiza también (`CK_DetallesReporte_Ubicacion`). La referencia admite hasta 500 caracteres, como en HU12.
- **Categoría original (CA07):** la elegida por el ciudadano queda en `CategoriaOriginalId` y es también la categoría vigente del caso (con el ciudadano como responsable de esa clasificación). La corrección por el operador (HU14) cambiará la vigente y conservará la original.
- **Independencia del SOS (CA06):** registrar un reporte no crea un SOS, no avisa a la sección de atención urgente y no aparece en ella. Los reportes se revisarán en la página «Reportes» (bloque 4, E5).
- **Idempotencia (CA08):** el mismo mecanismo que el SOS (S1-T14): `IdSolicitud` único por ciudadano, 201 la primera vez y 200 con el mismo reporte en un reenvío. Un identificador ya usado en un SOS del mismo ciudadano se rechaza en lugar de devolver ese SOS.
- **Registros (RNF07):** solo el identificador del caso y la fuente de la ubicación; nunca la descripción, la referencia ni las coordenadas.
- **Historial (HU27):** un reporte muestra «Reporte registrado como «tipo»» con su ciudadano, la fuente de la ubicación del hecho, la referencia y, en la cabecera, la fecha y hora aproximadas del hecho.
- **Pendiente para el bloque 3 (app):** formulario, mapa, advertencia de usar SOS ante peligro inmediato (CA05) y elección explícita de la posición actual (CA04).

### Decisiones y observaciones del bloque 3

- **Acceso:** el inicio del ciudadano tiene, debajo del SOS, «¿No es urgente? Reportar una incidencia». El formulario empieza con la advertencia de usar el SOS ante peligro inmediato y un botón «Ir al SOS» (CA05).
- **Ubicación del hecho (CA03, CA04):** nada viene elegido por defecto. «Marcar en el mapa» muestra un mapa Leaflet incluido en la app (sin CDN) con teselas de OpenStreetMap (E3); un toque fija el punto. «Escribir una referencia» oculta el mapa y borra cualquier punto. «Usar mi posición actual» es el único lugar donde la app lee la posición del dispositivo para un reporte: pide el permiso solo si no estaba decidido y, si lo obtiene, muestra el punto en el mapa con su precisión. Si el ciudadano mueve ese punto, pasa a ser un punto del mapa. Abrir el formulario no lee la posición. La referencia es opcional junto a un punto y obligatoria sola.
- **Fecha y hora:** por defecto, el momento actual del teléfono; no se puede elegir una fecha futura y una hora futura se marca antes de enviar (DP08).
- **Envío (CA02, CA08):** se comprueban los mismos datos que en el servidor para avisar sin esperar la red, y los errores que devuelva el servidor aparecen en su campo. El envío reutiliza el mismo `IdSolicitud` en los reintentos automáticos y en «Reintentar el mismo reporte». Mientras no hay confirmación, el formulario no admite otro envío con datos nuevos, porque podría estar ya registrado. La confirmación solo aparece con la respuesta del servidor.
- **Mapa y OpenStreetMap:** la página del mapa se carga con la dirección del servidor CAD como base, para que las peticiones de teselas indiquen su origen. Sin conexión, el mapa no muestra teselas; la referencia sigue disponible.
- **Avisos de Chromium:** al cerrar el formulario, Android finaliza el proceso del WebView y Chromium lo anota como «Renderer process crash detected (code -1)». Es la limpieza normal: el sistema registra «isolated not needed» y el proceso sale con código 0. La app no se ve afectada.
- **Entorno de prueba:** `arrancar-servidor.ps1` genera una clave JWT nueva en cada arranque, así que reiniciar el servidor invalida la sesión de la app (la app lo trata como sesión terminada, correctamente). Para probar el reintento sin servidor se usó un proxy local (127.0.0.1:5221 → 5220) que se cortó y se reanudó.

### Decisiones y observaciones del bloque 4

- **Página «Reportes» (E5):** `/panel/reportes` lista los reportes «Recibido» y «En revisión», los más recientes primero (como el listado de casos de DP17), con categoría vigente, ciudadano, fecha aproximada del hecho y forma de la ubicación. El detalle `/panel/reportes/{id}` muestra la descripción, la ubicación del hecho (con un mapa si es un punto; la referencia sin marcador, DP10) y las acciones de HU14. Enlace «Reportes» en la navegación del operador. No se actualiza en tiempo real: los reportes no son urgentes.
- **Inicio de la revisión (CA01, CA02):** solo desde «Recibido»; pasa a «En revisión» y el evento `RevisionIniciada` guarda el responsable, la fecha y la hora. Es un evento público porque «En revisión» es un estado visible para el titular (DP05). Si el mismo operador reenvía el formulario, no se registra nada nuevo; si otro operador ya la inició, se le informa quién.
- **Revisión simultánea (CA05):** el token `xmin` del caso detecta el conflicto y la nueva evaluación encuentra la revisión ya iniciada: un único cambio de estado y un único responsable (prueba determinista con dos operadores y otra con diez a la vez).
- **Corrección de categoría (CA03, CA04):** solo en «En revisión» y solo con categorías del catálogo. Cada corrección queda en `CorreccionesCategoria` (solo inserción: anterior, nueva, responsable y momento); la original del ciudadano sigue en el detalle del reporte y no cambia aunque haya varias correcciones. Elegir la categoría que ya tiene no registra nada. HU14 no limita quién corrige: cualquier operador, no solo quien inició la revisión.
- **Historial (HU27):** el reporte muestra «Revisión iniciada» y «Categoría corregida de «A» a «B»», con responsable y hora. En los reportes ya no aparece la entrada genérica «Clasificado como …».
- **Corrección de estilo en el panel:** a 390 px, una tabla ancha desbordaba la página. Ocurría también en «Cuentas institucionales» (Sprint 1): la rejilla de la página dejaba que la tabla ensanchara su columna y el texto visualmente oculto de las celdas escapaba del contenedor. Ahora la tabla se desplaza dentro de su marco.
