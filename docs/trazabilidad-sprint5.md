# Trazabilidad del Sprint 5

Sprint 5: cancelación, bienestar, avisos, tiempos, evaluación y mapa de concentración.
IDs de tarea y resultados verificables tomados del Plan Scrum (apartado 3.2). Estados según el Plan Scrum 3.3: *Pendiente*, *En curso*, *Implementada*, *Integrada*, *Aceptada*.

> **Calendario:** el del Plan Scrum (apartado 4.5: del 17/10 al 26/10/2026). El desarrollo empezó el 16/10/2026, con los Sprints 3 y 4 pendientes de su Sprint Review. Las evidencias de este documento llevan la fecha en que se registraron.

> **Sprints 3 y 4:** implementados y demostrados en local; pendientes de aceptación ([trazabilidad-sprint3.md](trazabilidad-sprint3.md), [trazabilidad-sprint4.md](trazabilidad-sprint4.md)).

**Objetivo del sprint (Plan Scrum):** incorporar los Should y Could previstos (cancelación, bienestar, avisos, tiempos, evaluación y mapa de concentración) y verificar las cláusulas de aceptación que quedaron pendientes de sprints anteriores.

**Historias seleccionadas (36 SP):** HU09 (5), HU40 (3), HU41 (5), HU42 (5), HU43 (5), HU28 (3), HU32 (3), HU33 (2) y HU29 (5).

**Condiciones de aceptación (Plan Scrum):** cada HU con todos sus CA verificados; HU27-CA02 solo se acepta tras verificar HU09 y HU17-CA04 solo tras verificar HU29; HU43 con DP19 sin resolver mantiene pendiente su criterio de caso urgente (DP19 la decidió el equipo por delegación del PO); HU03 se acredita únicamente si se completa la verificación de CA04 sobre HU04–HU43.

## Estado de las tareas

| ID | HU | Tarea | Estado | Evidencia |
|---|---|---|---|---|
| S5-T01 | HU09 | Cancelación de un SOS pendiente por el ciudadano | Implementada | `POST /api/casos/mios/{id}/cancelacion` (solo Ciudadano, solo el titular): el SOS «Pendiente» pasa a «Cancelada» y se guardan motivo, fecha y hora en `CancelacionesSos` (una por caso; motivo obligatorio también por la base de datos) con el evento público `SosCancelado` (CA01, CA04). La central recibe el aviso `SosCancelado` sin recargar: lo retira de la sección urgente y lo anuncia con un aviso visible y un tono distinto (CA02). Asignado o posterior: 409 sin cambios (CA03); ajeno o inexistente: 404 (CA05); un reenvío devuelve la cancelación ya registrada (CA08); un acompañamiento activo sigue activo (CA07). En la app, «Cancelar este SOS» en el detalle de «Mis casos» mientras está «Recibido» ([capturas](capturas/movil/75-hu09-cancelado.png)). `CancelacionSosTests` (servidor 22, app 13). |
| S5-T02 | HU09 | Concurrencia entre cancelación y asignación | Implementada | El token xmin del caso decide: si la asignación confirma primero, la cancelación se vuelve a evaluar y se rechaza (409); si confirma la cancelación, la asignación se rechaza con «El ciudadano canceló este SOS». Caso, cancelación y evento van en una transacción: sin registros parciales (`CA06_S5T02_*`: los dos órdenes forzados y 10 rondas simultáneas). |
| S5-T03 | HU09 | Vínculos de aceptación (HU11-CA02, HU20-CA06, HU34-CA07) y cláusula «Cancelada» de HU27 | Implementada (interpretación de los vínculos por confirmar con el PO) | Las HU no dicen qué parte de HU11-CA02, HU20-CA06 y HU34-CA07 depende de HU09 (el Plan Scrum pide confirmarlo con las HU originales). Se verificó: **HU11-CA02**, la cancelación llega a la central sin recargar, con aviso visual y sonoro (`CA02_*` y verificación en Chrome); **HU20-CA06**, un SOS con intentos «Sin respuesta» sigue siendo asignable, pero cancelado por su titular ya no (`S5T03_HU20_*`); **HU34-CA07**, la sesión de acompañamiento que el servidor registró sigue activa tras cancelar el SOS (`CA07_*`). **HU27-CA02:** el historial identifica el caso «Cancelada» con su resultado (motivo), responsable y momento, y la búsqueda de «Casos» filtra por «Cancelada» (`S5T03_HU27_CA02_*`). |
| S5-T04 | HU40 | Solicitud de confirmación de bienestar vinculada a una sesión activa, sin acciones automáticas | Pendiente | — |
| S5-T05 | HU41 | Dos respuestas del ciudadano y vínculo con un SOS existente, sin duplicados | Pendiente | — |
| S5-T06 | HU41 | Idempotencia ante respuestas repetidas | Pendiente | — |
| S5-T07 | HU42 | Evaluación temporal en el servidor de los avisos técnicos y de verificación, con deduplicación | Pendiente | — |
| S5-T08 | HU43 | Revisión de avisos y registro de su atención | Pendiente | — |
| S5-T09 | HU43 | Definición de origen y estado del caso urgente (HU43-CA05, DP19) | Pendiente | — |
| S5-T10 | HU28 | Tiempos de respuesta y de atención con las marcas registradas | Pendiente | — |
| S5-T11 | HU32 | Evaluación de un caso cerrado (1 a 5), una por caso, con control de concurrencia | Pendiente | — |
| S5-T12 | HU33 | Consulta de solo lectura de las evaluaciones con control de acceso | Pendiente | — |
| S5-T13 | HU29 | Mapa de concentración con filtros, exclusiones y diferenciación de origen | Pendiente | — |
| S5-T14 | HU17, HU29 | Verificación de HU17-CA04 con el mapa de HU29 | Pendiente | — |
| S5-T15 | HU03 | Verificación por rol de CA04 sobre HU04–HU43 | Pendiente | — |
| S5-T16 | Transversal | Revisión de registros del sprint | Pendiente | — |
| S5-T17 | HU09–HU43 | Integración, despliegue y demostración; lista consolidada de pendientes | Pendiente | — |

## Decisiones del Product Owner (16/10/2026)

Los documentos del proyecto no definen el contenido de DP18 ni la segunda parte de DP20. El PO respondió el 16/10/2026 a estas preguntas del desarrollo; DP12, DP19 y DP22 se decidieron el mismo día en el Sprint 4 ([trazabilidad-sprint4.md](trazabilidad-sprint4.md)).

| Tema | Decisión | Efecto |
|---|---|---|
| DP20 (segunda parte) — solicitudes simultáneas de bienestar (HU40-CA04) | Una sola confirmación pendiente por sesión: una nueva solicitud se rechaza mientras la anterior espera respuesta. | S5-T04. |
| DP18 — presentación de los tiempos (HU28) | Por caso: en su detalle y como columnas de la búsqueda de «Casos»; sin promedios. | S5-T10. |
| SOS vigente para «Necesito ayuda» (HU41-CA04; discrepancia D9 del Plan de Arquitectura) | Pendiente, Asignada, En camino y En atención (lo que el ciudadano ve como «Recibido» o «En atención»). | S5-T05. |
| DP12 — antigüedad de la posición (HU42-CA01) | 60 s (decidida en el Sprint 4). | S5-T07. |
| DP19 — caso urgente creado desde un aviso (HU43-CA05) | Delegada en el equipo de desarrollo (Sprint 4): origen nuevo «Acompañamiento», estado «Pendiente», el ciudadano de la sesión como titular, la última posición conocida como ubicación y la justificación del operador; sin despacho automático. | S5-T09. |
| DP14 — SOS repetidos | Decidida por delegación el 12/10/2026: cada activación crea un SOS; los reintentos con el mismo identificador son el mismo SOS. | HU41-CA07. |

## Plan de bloques

Mismo método que en los sprints anteriores: cada bloque con pruebas automatizadas, pruebas de mutación y verificación de extremo a extremo; commit, push y PR apilado por bloque.

| Bloque | Contenido | Tareas | Depende de |
|---|---|---|---|
| 1 | Planificación del sprint y cancelación de un SOS pendiente (HU09), con sus vínculos | S5-T01–S5-T03 | — |
| 2 | Evaluación de un caso cerrado (HU32) y consulta de evaluaciones (HU33) | S5-T11, S5-T12 | DP04 |
| 3 | Tiempos de respuesta y de atención (HU28) | S5-T10 | DP18 |
| 4 | Mapa de concentración (HU29) y verificación de HU17-CA04 | S5-T13, S5-T14 | DP10, DP17 |
| 5 | Confirmación de bienestar: solicitud (HU40) y respuesta (HU41) | S5-T04–S5-T06 | DP20, DP14 |
| 6 | Avisos del acompañamiento (HU42) y su atención (HU43) | S5-T07–S5-T09 | DP12, DP19 |
| 7 | Cierre del sprint: matriz completa (HU03), registros, demostración y lista consolidada de pendientes | S5-T15–S5-T17 | Bloques 1–6 |

## Interpretaciones para el PO

- **HU09 — estado que ve el ciudadano:** «Cancelado» (DP05 no lo nombra; sigue el patrón de «Cerrado», «Derivado» y «Descartado»), con el motivo como resultado. Al cancelarlo él mismo no recibe una notificación de HU31, igual que al registrarlo.
- **HU09 — qué se ofrece en la app:** «Cancelar este SOS» solo mientras el SOS está pendiente (para el ciudadano, «Recibido»), con un formulario de motivo y confirmación. Los reportes no se cancelan: HU09 habla solo de SOS.
- **HU09-CA02 — aviso a la central:** aviso visible en la sección urgente con enlace al historial y un tono distinto del de un SOS nuevo (si el operador activó los avisos sonoros). El motivo se lee en el historial.
- **Vínculos de S5-T03:** interpretación descrita en la tarea; pendiente de confirmar con las HU originales.

## Bloques ejecutados

| Bloque | Fecha | Tareas | Resultado |
|---|---|---|---|
| 1 — Cancelación de un SOS (HU09) | 16/10/2026 | S5-T01–S5-T03 | `dotnet test`: servidor **770/770** (27 nuevas: 22 de HU09 y 5 de la matriz) y app **238/238** (13 nuevas). Release: 0 advertencias. 15 mutaciones (12 del servidor y 3 de la app), todas detectadas. De extremo a extremo con el servidor local: la ciudadana emite un SOS en el emulador, intenta cancelarlo sin motivo (no se envía) y lo cancela con motivo; en Chrome, la central lo recibe y, al cancelarse, lo retira de la sección urgente y muestra «Ana Ficticia canceló su SOS» sin recargar; el historial lo marca «Cancelada» con el motivo y el detalle no ofrece asignarlo. Por la API: sin motivo 400, sereno 403, sin sesión 401, reenvío 200 con el motivo original. Los registros del servidor no contienen el motivo. |
