# Trazabilidad del Sprint 2

Sprint 2: verificación, despacho y ciclo de atención del SOS.
IDs de tarea y resultados verificables tomados del Plan Scrum (apartado 3.2). Estados según el Plan Scrum 3.3: *Pendiente*, *En curso*, *Implementada*, *Integrada*, *Aceptada*.

> **Calendario:** el Plan Scrum fija el Sprint 2 del 17/09/2026 al 26/09/2026. El desarrollo comienza el 13/10/2026. Las fechas reales las fija el Product Owner; este documento no las cambia.

> **Pendiente del Sprint 1 (en paralelo, a cargo del equipo):** S1-T02, S1-T08, S1-T18 y S1-T21 en el entorno publicado, y SPIKE01 en un teléfono Android real (ver [trazabilidad-sprint1.md](trazabilidad-sprint1.md)).

## Estado de las tareas

| ID | HU | Tarea | Estado | Evidencia |
|---|---|---|---|---|
| S2-T01 | HU12 | Intentos de contacto y verificación con tres resultados, sin sobrescribir | Implementada y verificada en local | `/panel/central/sos/{id}` (solo operador): teléfono registrado, intentos con tres resultados, información del hecho y referencia separada de las coordenadas (DP10). `VerificacionSosTests` CA01–CA08 + reenvío idempotente. Revisión en Chrome (escritorio y móvil). |
| S2-T02 | HU18 | Activar y desactivar el estado operativo | Pendiente | DP16 resuelta (ver decisiones). |
| S2-T03 | HU18 | Transmisión periódica de la ubicación con marca de tiempo | Pendiente | DP15: 30 s como valor inicial. |
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
| S2-T14 | HU03 | Verificación por rol de HU12–HU24 | En curso | Matriz ejecutable: rutas de HU12 añadidas (97 casos). |
| S2-T15 | HU11 | Repetir la medición de RNF02 | Pendiente | Necesita antes la medición formal del Sprint 1 (S1-T18). |
| S2-T16 | Transversal | Revisión de registros | En curso | `RevisionRegistrosTests` incluye HU12: ni el teléfono, ni la información del hecho, ni la referencia aparecen en los registros. |
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

### Decisiones y observaciones del bloque 1

- **Modelo:** `IntentoContacto`, de solo inserción, con el resultado (tres valores exactos, también restringidos por un `CHECK` en PostgreSQL), el operador, la hora y, opcionalmente, la información del hecho y la referencia. Cada intento deja un evento interno en el historial del caso (base de HU27).
- **Reenvíos:** el formulario lleva un identificador de solicitud con índice único `(CasoId, IdSolicitud)`: un doble envío no crea dos intentos. Es el mismo mecanismo de S1-T14.
- **Sin reglas añadidas:** HU12 no limita los intentos por estado del SOS ni condiciona la información al resultado, así que no se restringe. Las longitudes máximas (2000 y 500 caracteres) son límites técnicos de la base de datos, no reglas de negocio.
- **CA06:** un intento «Sin respuesta» o «No fue posible comunicar» no cambia el estado del SOS, que sigue en la sección urgente. La parte «puede asignarse» se verificará con HU20 (S2-T07).
- **CA08:** la autorización está en el servidor (política `OperadorAutorizado`). Un administrador con un token antifalsificación válido no puede registrar intentos, y un ciudadano o un sereno con su token tampoco pueden consultar ni registrar.
