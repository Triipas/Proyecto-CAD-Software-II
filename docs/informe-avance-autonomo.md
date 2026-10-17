# Informe del avance autónomo — cierre del Sprint 3 y Sprints 4 y 5

Fecha: 17/10/2026. Trabajo hecho por bloques en ramas propias, con commit, push y un PR apilado por bloque. Nada se fusionó, desplegó ni configuró fuera del repositorio. De Render solo se consultó `/salud` (respondió «Healthy»).

## Resumen

| Sprint | Estado | PR | Pruebas al cierre | Mutaciones |
|---|---|---|---|---|
| 3 | Bloque 9 (cierre) hecho; **pendiente de aceptación** | #26 | servidor 608/608, app 188/188 | 3 de 3 detectadas |
| 4 | Bloques 1–9 hechos; **pendiente de aceptación** | #27–#35 | servidor 743/743, app 225/225 | 78: 77 detectadas y 1 equivalente |
| 5 | Bloques 1–7 hechos; **pendiente de aceptación** | #36–#42 | servidor **895/895**, app **263/263** | 83: 78 detectadas y 5 equivalentes |

- **Release:** 0 advertencias en las dos soluciones (`Cad.slnx` y `CadMovil.slnx`) al cierre de cada bloque.
- **Matriz de autorización:** 292 casos (58 operaciones × 5 perfiles y 2 comprobaciones globales). Las 40 historias HU04–HU43 están verificadas por rol, así que **HU03-CA04 queda completa**.
- **Verificación de extremo a extremo:** cada bloque se probó contra el servidor local (PostgreSQL 17 en Docker), con el panel en Chrome (Playwright) y la app en el emulador Android 15 `cad_pixel`.
- **Evidencias:** las capturas están en `docs/capturas/` y el detalle de cada bloque, en `docs/trazabilidad-sprint{3,4,5}.md`.

## Sprint 3 — cierre (bloque 9)

| Bloque | Tareas | Commit | Rama | PR |
|---|---|---|---|---|
| 9 — Matriz por rol, registros y demostración | S3-T16–S3-T18 | `840b8e2` | `sprint3/cierre-sprint3` | #26 |

- **S3-T16, matriz:** HU10, HU14–HU17, HU20-CA07, HU26, HU27, HU30 y HU31 verificadas por rol.
- **S3-T17, registros:** sin datos sensibles. La prueba detecta las 3 fugas introducidas a propósito.
- **S3-T18, demostración:** `docs/demo-sprint3.md`.
- **Pendientes:** HU27-CA02 y HU17-CA04 quedaron para el Sprint 5 y ya están verificadas (ver abajo). S3-T19, S3-T20 y S3-T21 los ejecuta el equipo.

## Sprint 4 — acompañamiento, supervisión, mapa de casos y cuentas

| Bloque | Contenido | Commit | Rama | PR |
|---|---|---|---|---|
| 1 | Planificación y HU05 (desactivar cuenta) | `9f5b1c3` | `sprint4/hu05-desactivacion` | #27 |
| 2 | HU34–HU37 en el servidor | `b0cc74e` | `sprint4/hu34-37-acompanamiento-servidor` | #28 |
| 3 | HU34–HU37 en la app | `0cc059f` | `sprint4/hu34-37-acompanamiento-app` | #29 |
| 4 | Decisiones del PO (DP03, DP06, atención de reportes por el operador) | `6083312` | `sprint4/decisiones-po` | #30 |
| 5 | HU38 y HU39-A (DP12, DP20) | `20f87a2` | `sprint4/hu38-39a-supervision` | #31 |
| 6 | HU39-B (DP22) | `5b3be31` | `sprint4/hu39b-conservacion` | #32 |
| 7 | DP13: transmisión en segundo plano | `578d7e5` | `sprint4/acompanamiento-segundo-plano` | #33 |
| 8 | HU25: mapa de casos | `61d1c90` | `sprint4/hu25-mapa-casos` | #34 |
| 9 | Cierre (S4-T16–S4-T19) | `b987b63` | `sprint4/cierre-sprint4` | #35 |

- **HU con todos sus CA implementados y verificados (local):** HU05, HU25, HU34, HU35, HU36, HU37, HU38, HU39-A y HU39-B.
- **Compuerta de liberación del acompañamiento (S4-T16):** sigue en **demostración controlada**. Para liberarlo falta la prueba en un teléfono real y la decisión del PO.

## Sprint 5 — cancelación, evaluación, tiempos, mapa de concentración, bienestar y avisos

| Bloque | Contenido | Commit | Rama | PR |
|---|---|---|---|---|
| 1 | Planificación y HU09 (cancelar un SOS pendiente) | `10e20ff` | `sprint5/hu09-cancelar-sos` | #36 |
| 2 | HU32 (evaluar) y HU33 (consultar la evaluación) | `cb31545` | `sprint5/hu32-hu33-evaluacion` | #37 |
| 3 | HU28 (tiempos por caso, DP18) | `fcfe8e4` | `sprint5/hu28-tiempos` | #38 |
| 4 | HU29 (mapa de concentración) y HU17-CA04 | `fbcea29` | `sprint5/hu29-mapa-concentracion` | #39 |
| 5 | HU40 y HU41 (confirmación de bienestar, DP20) | `bd5a92c` | `sprint5/hu40-hu41-bienestar` | #40 |
| 6 | HU42 y HU43 (avisos y su atención; caso urgente de DP19) | `e85d964` | `sprint5/hu42-hu43-avisos` | #41 |
| 7 | Cierre (S5-T15–S5-T17) | `479beb9` y el commit de este informe | `sprint5/cierre-sprint5` | #42 |

### Estado por historia

| HU | CA | Estado | Verificación de extremo a extremo |
|---|---|---|---|
| HU09 | CA01–CA08 | Implementados y verificados | Emulador y Chrome: cancelación con motivo, aviso en la central sin recargar, historial «Cancelada». |
| HU27-CA02 | «Cancelada» | Verificada con HU09 | El historial identifica el caso cancelado con su resultado. |
| HU32 | CA01–CA06 | Implementados y verificados | Emulador: sin puntuación no se envía; evaluación 4/5 con comentario. |
| HU33 | CA01–CA03 | Implementados y verificados | Chrome: el operador ve la evaluación; el administrador no. |
| HU28 | CA01–CA04 | Implementados y verificados (DP18) | Chrome: columnas en «Casos» y «Tiempos del caso» en los detalles. |
| HU29 | CA01–CA05 | Implementados y verificados | Chrome: totales iguales a la base de datos; sin cancelados ni descartados. |
| HU17-CA04 | Exclusión del mapa | Verificada con HU29 | Un descartado no es asignable ni aparece en el mapa. |
| HU40 | CA01–CA04 | Implementados y verificados (DP20) | Chrome: solicitud, una sola pendiente por sesión. |
| HU41 | CA01–CA07 | Implementados y verificados | Emulador: presentación, «Estoy bien», notificación en segundo plano, «Necesito ayuda» vinculado o con SOS nuevo, sin duplicados. |
| HU42 | CA01–CA06 | Implementados y verificados (DP12) | Emulador en modo avión: el servidor generó los dos avisos sin repetirlos. |
| HU43 | CA01–CA06 | Implementados y verificados (DP19, decisión del equipo) | Chrome: revisión, actuación, caso urgente con justificación, vínculo y resolución. |
| HU03 | CA04 | Completa (HU04–HU43) | `MatrizAutorizacionTests`: 292 casos. |

**Ningún CA queda «Pendiente (DPxx)»:** las decisiones que faltaban (DP18, la segunda parte de DP20 y el SOS vigente de HU41) las dio el PO el 16/10/2026. DP19 la decidió el equipo por delegación.

### Defectos encontrados por las verificaciones y corregidos

- **Navegación a 390 px (bloque 4):** con cinco enlaces, la navegación del operador desbordaba la página. Ahora pasa a su propia línea.
- **Presentación en segundo plano (bloque 5):** la presentación de una confirmación se registraba con la app en segundo plano. Ahora se atiende a la pausa y la reanudación de la ventana, y hay una prueba nueva.

### Mutaciones equivalentes del Sprint 5 (justificadas)

- **Bloque 3:** medir el SOS desde una decisión de revisión no cambia nada, porque un SOS nunca tiene una.
- **Bloque 4:** tomar la ubicación del dispositivo en un reporte no cambia nada, porque un reporte nunca la guarda.
- **Bloque 5:** sin la comprobación previa de «otra pendiente», el índice único da el mismo rechazo.
- **Bloque 6 (2):** sin las comprobaciones previas de la condición o de la actuación repetida, los índices únicos dan el mismo resultado.

## Orden de fusión de los PR

Fusionarlos en este orden: **#26 → #27 → … → #35 → #36 → … → #42**.

Para cada uno:

1. Reapuntarlo a `main` (*Edit* → base `main`), o fusionar antes el anterior y borrar su rama.
2. Esperar a que la CI quede en verde. La CI solo se ejecuta en PR contra `main`, así que corre al reapuntarlo.
3. Fusionar con **«Create a merge commit»** (no squash ni rebase), para conservar los commits de cada bloque.

Render despliega `main` y aplica las migraciones al arrancar:

- Sprint 4: `Acompanamiento`, `AsignacionCancelada`, `UltimoContactoAcompanamiento` y `ConservacionRecorridos`.
- Sprint 5: `CancelacionSos`, `EvaluacionCaso`, `ConfirmacionBienestar` y `AvisosAcompanamiento`.

## Preguntas para el Product Owner (consolidadas)

1. **Aceptación:** aceptar o rechazar las historias de los Sprints 3, 4 y 5. Los guiones están en `docs/demo-sprint{3,4,5}.md`.
2. **HU03:** acreditarla, ya que su CA04 está completa.
3. **Vínculos de S5-T03** (HU11-CA02, HU20-CA06, HU34-CA07 con HU09): el Plan Scrum pide confirmarlos con las HU originales. Se verificaron según la interpretación anotada en la trazabilidad.
4. **Interpretaciones del Sprint 4:**
   - DP03: el caso vuelve a «Pendiente» al cancelarse la asignación.
   - DP20: «sin conexión» son más de 60 s sin envíos.
   - HU25: el mapa muestra solo los casos en curso.
   - DP22 (decisión del equipo): 30 días tras el cierre.
5. **Interpretaciones del Sprint 5:**
   - HU09: estado público «Cancelado»; solo se cancelan SOS.
   - HU32: una evaluación no se modifica; solo se evalúan casos «Cerrada».
   - HU28: los tiempos se miden desde el registro, hasta el primer inicio de atención.
   - HU29: representación por zonas de unos 550 m; los derivados entran.
   - HU41: se acepta la respuesta aunque la sesión haya terminado; la notificación no tiene botones.
   - HU42: «sin posiciones» se mide por la hora de obtención.
   - HU43: el primer revisor queda como responsable.
   - DP19 (decisión del equipo): el caso urgente tiene origen «Acompañamiento», está en la sección urgente y no se anuncia como SOS nuevo.
6. **Liberación del acompañamiento:** decidir si pasa a «Liberada» (Plan Scrum 3.3) cuando se pruebe en un teléfono real.

## Tareas del equipo

- Fusionar los PR #26–#42 en el orden indicado.
- **S3-T19:** medición de RNF02 en Render.
- **S3-T20:** SPIKE01 en un teléfono Android real. Incluye la transmisión en segundo plano (DP13), el ahorro de batería de cada fabricante y la notificación de la confirmación de bienestar.
- **S3-T21:** notificación push del despacho con la app cerrada.
- Generar y repartir el APK nuevo, y hacer el recorrido de las demostraciones en el entorno publicado.

## Lo que no se hizo

- **Copia del Plan Scrum con control de cambios (opcional):** no se hizo. El seguimiento de tareas y decisiones está al día en `docs/trazabilidad-sprint{3,4,5}.md`. Si se quiere, se prepara en una copia nueva del documento, sin tocar el original.
- **Fuera de las autorizaciones:** nada en Render, GitHub, Firebase ni Neon, salvo la consulta de `/salud`. No se fusionó ningún PR, no se borraron ramas ni se reescribieron commits.

## Notas

- **Datos de prueba locales:** para verificar HU41-CA03 se cancelaron, con HU09, los SOS pendientes que tenía la ciudadana ficticia en la base local `cad_movil_e2e`. También se registró una ciudadana ficticia nueva en esa misma base. Nada de esto toca el entorno publicado.
- **Seguridad:** no se registraron secretos ni datos personales. Las revisiones de registros de los tres sprints lo comprueban con pruebas automáticas que detectan fugas introducidas a propósito.
- **Archivos sin incluir en los commits:** `.claude/`, `skills-lock.json` y `google-services.json`.
- **Identidad de Git:** se mantuvo la del autor (Triipas), sin atribución a Claude.
