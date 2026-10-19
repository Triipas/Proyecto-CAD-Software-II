# Trazabilidad del Sprint 4

Sprint 4: acompañamiento temporal, supervisión y alcance funcional Must restante.
IDs de tarea y resultados verificables tomados del Plan Scrum (apartado 3.2). Estados según el Plan Scrum 3.3: *Pendiente*, *En curso*, *Implementada*, *Integrada*, *Aceptada*.

> **Calendario:** el del Plan Scrum (apartado 4.5: del 07/10 al 16/10/2026). El desarrollo empezó el 16/10/2026, con el Sprint 3 aún pendiente de su Sprint Review. Las evidencias de este documento llevan la fecha en que se registraron.

> **Sprint 3:** implementado, integrado en `main` y demostrado en local; pendiente de aceptación ([trazabilidad-sprint3.md](trazabilidad-sprint3.md)).

**Objetivo del sprint (Plan Scrum):** cubrir funcionalmente el resto de los Must: acompañamiento temporal con sus controles de privacidad, supervisión por el operador, mapa de casos, desactivación de cuentas y retención de recorridos, en condición de demostración controlada si faltan condiciones.

**Historias seleccionadas (39 SP):** HU34 (5), HU35 (5), HU36 (5), HU37 (5), HU25 (5), HU38 (5), HU39-A (3), HU05 (3) y HU39-B (3).

**Condiciones de aceptación (Plan Scrum):** cada HU con todos sus CA verificados, con HU39-B condicionada a DP22. El acompañamiento no se habilita para uso real sin HU36, HU37, el acceso restringido de HU38 y la retención de HU39-B; si falta alguna, solo demostración controlada. Cobertura funcional Must prevista, pero no cierre de aceptación del MVP. HU03 no se acredita al cierre de este sprint.

## Estado de las tareas

| ID | HU | Tarea | Estado | Evidencia |
|---|---|---|---|---|
| S4-T01 | HU34 | Activación con sesión registrada por el servidor, 30 minutos, estado persistente y una sola sesión | Pendiente | — |
| S4-T02 | HU34 | Estado mostrado real, sin prometer cobertura fuera del entorno validado | Pendiente | — |
| S4-T03 | HU35 | Envío de la posición cada 15 s con hora y precisión | Pendiente | — |
| S4-T04 | HU35 | Estados reales de la transmisión según SPIKE01; reestimación de HU35 si procede | Pendiente | — |
| S4-T05 | HU36 | Finalización por el ciudadano: detención local inmediata, cierre en el servidor y cierre sin conexión | Pendiente | — |
| S4-T06 | HU36 | Rechazo de posiciones posteriores al cierre (regla de cierre compartida) | Pendiente | — |
| S4-T07 | HU37 | Cierre automático a los 30 minutos en el servidor, sin renovación y reflejado al reconectar | Pendiente | — |
| S4-T08 | HU25 | Mapa de casos con marcadores por origen y estado, actualizados; la referencia no genera marcador (DP10) | Pendiente | — |
| S4-T09 | HU38 | Acompañamientos activos con posición, hora, precisión, antigüedad y advertencia | Pendiente | — |
| S4-T10 | HU38 | Acceso restringido en solicitudes directas | Pendiente | — |
| S4-T11 | HU39-A | Historial de sesiones separado del historial de casos, con acceso restringido | Pendiente | — |
| S4-T12 | HU05 | Desactivación de una cuenta institucional y rechazo de accesos posteriores, conservando sus registros | Implementada | Sección «Estado de la cuenta» en `/panel/admin/cuentas/{id}/editar` (solo administrador) con confirmación; la cuenta pasa a «Desactivada» y ya no obtiene token en la app ni ingresa al panel. `DesactivacionCuentaTests` CA01, CA02, CA05. |
| S4-T13 | HU05 | HU05-CA03 con el historial de HU27; sesión abierta o asignación vigente según DP03 | Implementada (CA03); **CA04 pendiente (DP03)** | Un sereno que atendió y cerró un caso se desactiva: sus eventos, asignación y cierre se conservan, el historial lo sigue identificando y la búsqueda por sereno encuentra el caso (`DesactivacionCuentaTests.CA03_*`). CA04: sin regla hasta que el PO decida DP03 (propuesta en «Decisiones pendientes»). |
| S4-T14 | HU39-B | Registrar la decisión de DP22 antes de implementar | **Bloqueada (DP22)** | Sin plazo de conservación definido; no se supone ninguno. |
| S4-T15 | HU39-B | Eliminación por plazo y conservación excepcional | **Bloqueada (DP22)** | Depende de S4-T14. |
| S4-T16 | HU34–HU39-B | Compuerta de liberación del acompañamiento | Pendiente | — |
| S4-T17 | HU03 | Verificación por rol de HU05, HU25 y HU34–HU39 | Pendiente | — |
| S4-T18 | Transversal | Revisión de registros: sin posiciones fuera del acompañamiento activo ni contraseñas | Pendiente | — |
| S4-T19 | HU05, HU25, HU34–HU39-B | Integración, despliegue y demostración controlada | Pendiente | — |

## Decisiones pendientes y propuestas para el PO

El Plan Scrum (Sprint Planning del Sprint 4) pide revalidar DP03, DP04, DP10, DP12, DP13, DP20 y DP22. Los documentos del proyecto no definen el contenido de DP12 ni de DP13: se deduce su tema por las historias que los citan. Ninguna propuesta de esta tabla está aceptada; el desarrollo implementa lo que no depende de ellas y deja cada criterio afectado marcado como pendiente.

| Tema | Situación | Propuesta del equipo de desarrollo | Criterios afectados |
|---|---|---|---|
| DP03 — cuenta desactivada con sesión abierta o asignación vigente (HU05-CA04) | Pendiente | Al desactivar, renovar el sello de seguridad de la cuenta y comprobarlo en cada solicitud, de modo que la sesión abierta (panel o app) termine de inmediato. Con una asignación vigente, rechazar la desactivación hasta que el caso se cierre o se reasigne. | HU05-CA04 |
| DP04 — «operador autorizado» (HU38, HU39) | Decidida por delegación el 12/10/2026 | Cualquier cuenta activa con rol Operador (política `OperadorAutorizado`). | — |
| DP10 — referencia textual en el mapa (HU25) | Supuesto aceptado por el PO el 07/10/2026 | La referencia no genera marcador. | HU25-CA04 |
| DP12 — sin contenido en los documentos; HU38 y HU42 lo citan (antigüedad de la posición) | Pendiente | Considerar desactualizada una posición con más de 60 s, el umbral que ya fija HU42-CA01, configurable. | HU38-CA02, HU38-CA03 |
| DP13 — sin contenido en los documentos; HU34 y HU35 lo citan (condiciones de la transmisión) | Pendiente | Entorno compatible del MVP = app en primer plano y con conexión (resultado de SPIKE01 en el emulador). La interfaz lo dice y no promete más (HU35-CA05). | HU34-CA01, HU35-CA01, HU35-CA05 |
| DP20 — cierre sin conexión (HU36-CA03) | Pendiente | La app detiene la captura y el envío al instante y guarda el cierre para enviarlo al recuperar la conexión. Mientras tanto la sesión sigue activa en el servidor hasta que llegue el cierre o venza (la central ve que la posición envejece). | HU36-CA03 (efecto mientras tanto) |
| DP22 — plazo de conservación de recorridos (HU39-B, RNF13) | Pendiente y bloqueante | Ninguna: el plazo lo fija el PO o la municipalidad. Sin él no se implementa la eliminación y el acompañamiento solo se presenta como demostración controlada. | HU39-B (CA04, CA05) |

## Plan de bloques

Mismo método que en los sprints anteriores: cada bloque con pruebas automatizadas, pruebas de mutación y verificación de extremo a extremo; commit, push y PR apilado por bloque.

| Bloque | Contenido | Tareas | Depende de |
|---|---|---|---|
| 1 | Planificación del sprint y desactivación de cuentas institucionales (HU05) | S4-T12, S4-T13 | DP03 (CA04) |
| 2 | Acompañamiento en el servidor: activación, posiciones, finalización y vencimiento (HU34–HU37) | S4-T01, S4-T03, S4-T05, S4-T06, S4-T07 | DP13, DP20 |
| 3 | Acompañamiento en la app: activación con finalidad, envío cada 15 s, finalización y estado real (HU34–HU37) | S4-T01–S4-T07 (app) | Bloque 2; SPIKE01 |
| 4 | Supervisión en el panel: acompañamientos activos (HU38) e historial de sesiones (HU39-A) | S4-T09–S4-T11 | Bloque 2; DP04, DP12 |
| 5 | Mapa de casos (HU25) | S4-T08 | DP10, DP17 |
| 6 | Cierre del sprint: compuerta de liberación, matriz, registros y demostración controlada | S4-T16–S4-T19 | Bloques 1–5 |

**HU39-B no se implementa:** S4-T14 y S4-T15 quedan bloqueadas por DP22. Mientras tanto, las posiciones del acompañamiento se conservan sin plazo de eliminación, y por eso el acompañamiento no puede habilitarse para uso real (S4-T16).

**Riesgo (SPIKE01):** la transmisión solo está validada con la app abierta y con conexión, en el emulador. Se implementa como en HU18 (envío con la app en primer plano) y la interfaz no promete más. La prueba en un teléfono real es S3-T20 (equipo).

## Bloques ejecutados

| Bloque | Fecha | Tareas | Resultado |
|---|---|---|---|
| 1 — Planificación y desactivación de cuentas (HU05) | 16/10/2026 | S4-T12, S4-T13; S4-T17 (parcial) | `dotnet test`: servidor **622/622** (14 nuevas; sin rutas nuevas: la acción comparte la ruta de la ficha, ya en la matriz). Release: 0 advertencias. 7 mutaciones detectadas: sin cambiar el estado, la app o el panel aceptan una cuenta desactivada, desactivar una cuenta que no es institucional, página abierta a cualquier sesión, correo en el registro y sin la nueva evaluación ante un conflicto (sobrevivía con la prueba en paralelo; se detecta tras añadir una prueba determinista con un interceptor). Chrome contra el servidor local: el administrador crea un operador ficticio, que ingresa a la central; lo desactiva con la confirmación; el listado lo muestra «Desactivada» y el operador ya no ingresa («La cuenta está desactivada.»); 390 px sin desbordamiento; 0 errores de consola. Capturas: [confirmación](capturas/panel/hu05-confirmar-desactivacion.png), [listado](capturas/panel/hu05-listado-desactivada.png), [ingreso rechazado](capturas/panel/hu05-ingreso-rechazado.png). |

### Decisiones y observaciones del bloque 1

- **Dónde:** la desactivación está en la ficha de la cuenta (`/panel/admin/cuentas/{id}/editar`), en una sección «Estado de la cuenta». Se pide una confirmación explícita («Desactivar la cuenta…» → «Confirmar la desactivación»), sin JavaScript (`<details>`), porque la acción impide el acceso de una persona.
- **Qué cambia (CA01, CA02):** solo el estado de la cuenta. El inicio de sesión ya rechazaba las cuentas desactivadas desde HU02 (403 en la app; aviso en el panel), sin revelar el estado a quien no conoce la contraseña.
- **Registros conservados (CA03):** la cuenta no se borra; sus eventos, asignaciones y cierres siguen vinculados y visibles en el historial y en la búsqueda de casos (el filtro de serenos ya incluía las cuentas desactivadas).
- **Solo cuentas institucionales:** una cuenta de ciudadano o de administrador no existe para esta operación (404), igual que en HU04.
- **Repetición y simultaneidad:** desactivar una cuenta ya desactivada no es un error. Si dos administradores la desactivan a la vez, el sello de concurrencia de Identity detecta el conflicto y la nueva lectura encuentra la cuenta ya desactivada.
- **DP03 (CA04, pendiente):** no se aplica ninguna regla. Con el código actual, una sesión ya abierta sigue válida hasta que vence (el JWT de la app dura lo configurado; la cookie del panel, 8 horas) y una asignación vigente no cambia. La página lo advierte al administrador. La propuesta para el PO está en «Decisiones pendientes».
- **Sin reactivación:** HU05 no la pide y HU04 no la incluye entre los datos modificables; si el PO la quiere, es una historia o un criterio nuevo.
- **Registro:** solo «Cuenta institucional {id} desactivada.», sin correo ni nombre.
