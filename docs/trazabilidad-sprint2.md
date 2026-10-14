# Trazabilidad del Sprint 2

Sprint 2: verificación, despacho y ciclo de atención del SOS.
IDs de tarea y resultados verificables tomados del Plan Scrum (apartado 3.2). Estados según el Plan Scrum 3.3: *Pendiente*, *En curso*, *Implementada*, *Integrada*, *Aceptada*.

> **Calendario:** el Plan Scrum fija el Sprint 2 del 17/09/2026 al 26/09/2026. El desarrollo comienza el 13/10/2026. Las fechas reales las fija el Product Owner; este documento no las cambia.

> **Pendiente del Sprint 1 (en paralelo, a cargo del equipo):** S1-T02, S1-T08, S1-T18 y S1-T21 en el entorno publicado, y SPIKE01 en un teléfono Android real (ver [trazabilidad-sprint1.md](trazabilidad-sprint1.md)).

## Estado de las tareas

| ID | HU | Tarea | Estado | Evidencia |
|---|---|---|---|---|
| S2-T01 | HU12 | Intentos de contacto y verificación con tres resultados, sin sobrescribir | Implementada y verificada en local | `/panel/central/sos/{id}` (solo operador): teléfono registrado, intentos con tres resultados, información del hecho y referencia separada de las coordenadas (DP10). `VerificacionSosTests` CA01–CA08 + reenvío idempotente. Revisión en Chrome (escritorio y móvil). |
| S2-T02 | HU18 | Activar y desactivar el estado operativo | Implementada y verificada en local y en emulador | `GET`/`PUT /api/sereno/estado`: activación solo con el permiso de ubicación declarado (DP16-a); desactivación siempre permitida (DP16-b). `EstadoOperativoTests` CA02, CA03, CA05 y CA06 (parte sin asignación). App: `ServicioTurnoSereno` y pantalla del sereno (`TurnoSerenoTests`); emulador: explicación del permiso, activación y desactivación. |
| S2-T03 | HU18 | Transmisión periódica de la ubicación con marca de tiempo | Implementada y verificada en local y en emulador | `POST /api/sereno/posiciones`: solo con el sereno operativo; marca de tiempo del dispositivo y de recepción (CA01); reenvío sin duplicar. Intervalo de 30 s configurable (DP15), entregado a la app en `GET /api/sereno/estado`. App: una posición al activarse y otra cada 30 s; emulador: 3 posiciones en 60 s, envío detenido al desactivar y reanudado al reabrir la app. |
| S2-T04 | HU19 | Panel de estado y disponibilidad actualizado por eventos | Pendiente | Mapa base del Sprint 1 (decisión del 13/10/2026). |
| S2-T05 | HU20 | Asignar un caso pendiente a un sereno disponible | Pendiente | HU20 unificada (ver decisiones). |
| S2-T06 | HU20 | Concurrencia, idempotencia y ausencia de registros parciales | Pendiente | — |
| S2-T07 | HU12, HU20 | Vínculo verificación ↔ «puede asignarse» (CA06) | Pendiente | HU12-CA06 ya verifica que un intento sin respuesta no cancela el SOS; falta la asignación. |
| S2-T08 | HU13 | Catálogo y clasificación posterior con «Tipo por determinar» | Pendiente | DP07 y DP09 resueltas. |
| S2-T09 | HU21 | Notificación del despacho y consulta con límites | Pendiente | DP06 resuelta; notificación push decidida ([ADR-07](decisiones/ADR-07-notificaciones-push-despacho.md)). |
| S2-T10 | HU22 | Confirmar el desplazamiento | Pendiente | — |
| S2-T11 | HU23 | Registrar el inicio de atención | Pendiente | — |
| S2-T12 | HU24 | Cierre con tipificación y resultado; notas internas separadas | Pendiente | DP05 y DP07 resueltas. |
| S2-T13 | HU24 | Cierre por el operador y liberación de la disponibilidad | Pendiente | — |
| S2-T14 | HU03 | Verificación por rol de HU12–HU24 | En curso | Matriz ejecutable: rutas de HU12 y HU18 añadidas (112 casos). |
| S2-T15 | HU11 | Repetir la medición de RNF02 | Pendiente | Necesita antes la medición formal del Sprint 1 (S1-T18). |
| S2-T16 | Transversal | Revisión de registros | En curso | `RevisionRegistrosTests` incluye HU12 y HU18: ni el teléfono, ni la información del hecho, ni la referencia, ni las coordenadas del sereno aparecen en los registros. |
| S2-T17 | HU12–HU24 | Integración y demostración del ciclo del SOS | Pendiente | — |

## Decisiones del Product Owner (13/10/2026)

Respuestas a las decisiones E1–E12 de la planificación del Sprint 2. El Plan Scrum (.docx) se actualiza con las mismas decisiones, con control de cambios.

| Tema | Decisión | Efecto |
|---|---|---|
| E1 — PR #7 | Autorizado; ya estaba fusionado en `main` (a593150). | El Sprint 2 parte de `main`. |
| DP10 — referencia de ubicación (HU12, HU25) | **Supuesto aceptado:** la referencia obtenida por contacto se guarda como texto, separada de las coordenadas del dispositivo, y no genera marcador. | Implementado en S2-T01. |
| DP15 — intervalo del sereno (HU18) | **30 s** como valor inicial, configurable. | S2-T03. |
| DP16 — sereno sin ubicación o con asignación (HU18) | (a) **No** se puede activar el estado operativo sin permiso de ubicación. (b) Si un sereno con asignación vigente se desactiva, **se permite y la asignación sigue vigente**. No se definen otros efectos. | S2-T02. Consecuencia de las reglas ya existentes: al desactivarse deja de transmitir posiciones (HU18-CA02) y no figura como disponible (HU19-CA02). |
| Mapa (HU19, HU20) | Se usa el **mapa base del Sprint 1** para mostrar serenos y SOS (mínimo adelantado de HU25, como en HU11). **Sin rutas, líneas ni distancias:** el operador decide cuál es el sereno disponible más cercano. | S2-T04, S2-T05. |
| DP07 — catálogo (HU13, HU24) | **Supuesto:** el mínimo de RF16.2: violencia doméstica, robo, presunta actividad relacionada con drogas y «Otros». La tipificación final de HU24 usa **el mismo catálogo**. | S2-T08, S2-T12. |
| DP09 — quién clasifica (HU13) | **Solo el operador.** | S2-T08. |
| DP06 — datos visibles para el sereno (HU21) | **Mínimo aceptable:** identificador, origen, estado y hora; ubicación del dispositivo o «no disponible»; la referencia y la información del hecho que registró el operador. **No ve** el nombre ni el teléfono del ciudadano, ni las notas internas. | S2-T09. |
| E10 — aviso del despacho (HU21-CA01) | **Notificación push** al asignar, además del aviso en tiempo real con la app abierta. Cambia la exclusión de la arquitectura. | [ADR-07](decisiones/ADR-07-notificaciones-push-despacho.md). Requiere un proyecto de Firebase creado por el equipo. Puede requerir reestimar HU21. |
| DP05 — visibilidad para el ciudadano (HU24) | En el Sprint 2 se guardan por separado el resultado público y las notas internas, y ninguna ruta del ciudadano expone el caso. La vista del ciudadano llega con HU30 (Sprint 3). | S2-T12; la matriz lo verifica (S2-T14). |
| E12 — HU20-A / HU20-B | **Se unifica HU20.** Interpretación registrada, **pendiente de confirmar por el PO:** HU20 es una sola historia de 9 SP (8 + 1). Se implementa en el Sprint 2 para cualquier caso «Pendiente», sin distinguir el origen. Su CA07 (reporte admitido) solo puede verificarse cuando existan reportes admitidos (HU15, Sprint 3), así que HU20 se aceptará cuando se verifique CA07. | SP planificados: Sprint 2, de 36 a 37; Sprint 3, de 34 a 33 (total sin cambios: 181). La antigua tarea de HU20-B pasa a ser la verificación de HU20-CA07 con reportes. |

## Bloques ejecutados

| Bloque | Fecha | Tareas | Resultado |
|---|---|---|---|
| 1 — Verificación del SOS | 13/10/2026 | S2-T01 (HU12); S2-T14 y S2-T16 (parcial) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 234/234 superadas (32 nuevas). Pruebas de mutación detectadas: sin la política de operador (9 pruebas fallan); con `Enum.TryParse` para el resultado (CA03 falla con «1» y «contactado»); con la información del hecho en un registro de depuración (la auditoría de registros falla). Chrome (1366×768 y 390×844): foco en el resumen de errores, sin desbordamiento horizontal, sin errores de consola. Capturas: [escritorio](capturas/panel/hu12-verificacion-escritorio.png), [error en móvil](capturas/panel/hu12-error-movil.png). |
| 2 — Estado operativo y posiciones del sereno (servidor) | 13/10/2026 | S2-T02, S2-T03 (HU18, servidor); S2-T14 y S2-T16 (parcial) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 265/265 superadas (31 nuevas). Mutaciones detectadas: sin la regla DP16-a (falla CA03); aceptando posiciones sin estar operativo (3 pruebas fallan); sin la política de sereno (10 pruebas fallan); con las coordenadas en un registro de depuración (falla la auditoría). |
| 3 — Modo sereno en la app | 14/10/2026 | S2-T02, S2-T03 (HU18, app) | App Debug y Release: 0 advertencias, 0 errores. `Cad.Movil.Tests`: 94/94 superadas (19 nuevas; estables en 5 repeticiones). Mutaciones detectadas: sin comprobar el permiso (5 pruebas fallan), desactivar sin detener el envío (2), ignorar el 409 del servidor (1), sin límite de espera de la ubicación (1). Emulador Android 15 contra el servidor local: explicación previa del permiso; activación; una posición cada 30 s exactos (registros en PostgreSQL); desactivación sin más posiciones; reanudación al reabrir la app. Capturas en `docs/capturas/movil/20-*`, `21-*` y `22-*`. |

### Decisiones y observaciones del bloque 1

- **Modelo:** `IntentoContacto`, de solo inserción, con el resultado (tres valores exactos, también restringidos por un `CHECK` en PostgreSQL), el operador, la hora y, opcionalmente, la información del hecho y la referencia. Cada intento deja un evento interno en el historial del caso (base de HU27).
- **Reenvíos:** el formulario lleva un identificador de solicitud con índice único `(CasoId, IdSolicitud)`: un doble envío no crea dos intentos. Es el mismo mecanismo de S1-T14.
- **Sin reglas añadidas:** HU12 no limita los intentos por estado del SOS ni condiciona la información al resultado, así que no se restringe. Las longitudes máximas (2000 y 500 caracteres) son límites técnicos de la base de datos, no reglas de negocio.
- **CA06:** un intento «Sin respuesta» o «No fue posible comunicar» no cambia el estado del SOS, que sigue en la sección urgente. La parte «puede asignarse» se verificará con HU20 (S2-T07).
- **CA08:** la autorización está en el servidor (política `OperadorAutorizado`). Un administrador con un token antifalsificación válido no puede registrar intentos, y un ciudadano o un sereno con su token tampoco pueden consultar ni registrar.

### Decisiones y observaciones del bloque 2

- **Módulo nuevo `Serenos`:** `EstadosSereno` (una fila por sereno: operativo y hora del último cambio) y `PosicionesSereno` (solo inserción, con `CHECK` de rangos). La disponibilidad no se guarda: se calculará con HU19 (operativo y sin asignación vigente, HU19-CA02).
- **DP16-a:** el servidor no puede consultar el permiso de ubicación del sistema operativo. La app declara si lo tiene (`ubicacionAutorizada`) y lo comprobará antes de activar (bloque 3); el servidor rechaza la activación sin esa declaración. Es la única garantía posible en el servidor.
- **DP16-b:** desactivarse siempre está permitido y no toca asignaciones. Cuando exista HU20, una prueba comprobará que la asignación sigue vigente.
- **Posiciones:** se aceptan o rechazan según el estado del servidor en el momento de recibirlas, no según el reloj del dispositivo (podría estar desajustado). Con el sereno no operativo se responde 409 y no se guarda nada (CA02, CA03). La misma posición reenviada (mismo sereno y misma marca de tiempo) no se duplica.
- **Sin reglas añadidas:** HU18 no fija antigüedad máxima ni precisión mínima para las posiciones; solo se validan rangos de coordenadas, precisión positiva y marca de tiempo presente.
- **Precisión de tiempos:** la hora del último cambio se redondea a microsegundos (la precisión de PostgreSQL) para que la API devuelva lo mismo al cambiar que al consultar.
- **CA04 (disponibilidad que ve el operador):** el aviso en tiempo real a la central se añade con HU19 (bloque 4), que es quien lo muestra.
- **Pendiente sin decidir:** la conservación de las posiciones del sereno no está definida (DP22 trata los recorridos del acompañamiento). No se borra nada automáticamente.

### Decisiones y observaciones del bloque 3

- **`ServicioTurnoSereno`** (lógica de la app, probada con dobles y reloj simulado): el estado operativo lo decide el servidor. Al abrir el modo sereno se consulta y, si ya era operativo y hay permiso, se reanuda el envío sin volver a activar.
- **Activación (DP16-a):** sin permiso concedido no se llama al servidor. Si el permiso no se ha decidido, primero se explica y luego se solicita (como en HU06); si está denegado, se orienta a los ajustes.
- **Envío (CA01, DP15):** una posición actual al activarse y otra cada intervalo indicado por el servidor. Cada lectura de ubicación tiene el mismo límite de 10 s que el SOS (hallazgo de SPIKE01). Sin posición o sin conexión, ese envío se omite y se avisa: no se acumulan posiciones antiguas para reenviarlas. Si el servidor responde que el sereno no está operativo (409), el envío se detiene.
- **Desactivación (CA02, DP16-b):** primero se detiene el envío en el dispositivo y después se informa al servidor; si el servidor no responde, el envío sigue detenido y se puede reintentar.
- **Salir mientras está operativo:** el botón se deshabilita con un texto que explica por qué («Desactiva tu estado operativo antes de salir»), para que la central no vea un sereno operativo que no transmite. No se desactiva automáticamente al salir: sería un efecto que la HU no define.
- **Condición validada (SPIKE01):** la pantalla indica que la ubicación se comparte «mientras estés operativo y con la app abierta». No hay servicio en primer plano ni envío garantizado en segundo plano.
- **Estilo:** el botón secundario de la app tenía un estado deshabilitado idéntico al normal; ahora se atenúa.
- **Incidencias del entorno, no de la app:** al reiniciar Docker Desktop y el emulador en frío, la red del emulador quedó sin ruta al equipo hasta reiniciar su Wi-Fi (`svc wifi disable/enable`). Además, `pm clear` borra los ensamblados del despliegue rápido de Debug; para probar desde cero hay que desinstalar y reinstalar.
