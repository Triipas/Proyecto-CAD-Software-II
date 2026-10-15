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
| S3-T01 | HU10 | Registro de reporte con ubicación del hecho por mapa o por texto y categoría del catálogo compartido | Pendiente | DP07, DP08 y DP10 resueltas. |
| S3-T02 | HU10 | Posición del dispositivo solo por elección del ciudadano; idempotencia del registro | Pendiente | — |
| S3-T03 | HU26 | Listado de casos con cinco filtros combinables | Pendiente | DP17 resuelta. |
| S3-T04 | HU26 | «Tipo por determinar» y casos sin coordenadas en el listado | Pendiente | «Tipo por determinar» ya existe (HU13). |
| S3-T05 | HU14 | Inicio de revisión y corrección de categoría conservando la original, con control de concurrencia | Pendiente | — |
| S3-T06 | HU15 | Admisión de un reporte para atención | Pendiente | DP21 resuelta. |
| S3-T07 | HU16 | Derivación con destino y acción, sin integración externa | Pendiente | DP21 resuelta. |
| S3-T08 | HU20 (CA07) | Asignación de un reporte admitido con las reglas de HU20 | Pendiente | La asignación del Sprint 2 ya vale para cualquier caso «Pendiente». |
| S3-T09 | HU17 | Descarte con justificación obligatoria | Pendiente | DP05 y DP21 resueltas. |
| S3-T10 | HU17 | HU17-CA04 registrado como pendiente hasta el Sprint 5 (mapa de HU29) | Pendiente | — |
| S3-T11 | HU27 | Historial consolidado de SOS y reportes, notas internas restringidas, sin acompañamiento | En curso (implementado y verificado para los SOS) | `/panel/casos/{id}/historial` (solo operador): emisión, intentos de contacto, clasificación, asignación y su fin, avances y cierre, en orden y con responsable y hora; resultado final identificado; notas internas rotuladas. `HistorialTests`. Las actuaciones de los reportes se añaden con HU14–HU17. |
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

### Decisiones y observaciones del bloque 1

- **Orden del sprint:** el historial se empieza por los SOS porque no depende de ninguna decisión pendiente; la consulta está preparada para añadir las actuaciones de los reportes (revisión, corrección de categoría, admisión, derivación y descarte) en los bloques 4 y 5.
- **Fuente de las actuaciones:** se reúnen de los registros que ya existen (caso, intentos de contacto, clasificación, asignaciones, eventos de avance y cierre), sin duplicar datos. Cada actuación muestra su responsable y su marca de tiempo (CA01, CA03).
- **Resultado final (CA02):** un caso cerrado muestra arriba su estado, la fecha, la tipificación y el resultado público. «Cancelada», «Derivada» y «Descartada» se añaden con HU09, HU16 y HU17.
- **Notas internas (CA04):** solo las ve el personal autorizado y aparecen rotuladas. La página exige el rol de operador (CA06); un administrador, un sereno o un ciudadano no la obtienen, tampoco con una solicitud directa.
- **Acompañamiento (CA05):** el historial no incluye posiciones ni sesiones de acompañamiento (HU39).
- **Acceso:** enlace «Ver el historial del caso» en el detalle del SOS.
