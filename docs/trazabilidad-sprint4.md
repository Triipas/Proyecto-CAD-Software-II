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
| S4-T01 | HU34 | Activación con sesión registrada por el servidor, 30 minutos, estado persistente y una sola sesión | Implementada (servidor) | `POST /api/acompanamiento` (solo ciudadano): sesión «Activa» con inicio y vencimiento (+30 min) del servidor; sin finalidad aceptada o sin ubicación no se crea (400); una sola activa por ciudadano (índice único parcial; 409 con otra activa, también con 10 inicios simultáneos); reintento con el mismo `IdSolicitud` = la misma sesión; `GET /api/acompanamiento/actual` para el estado persistente. No crea casos. `AcompanamientoTests.HU34_*`. La app, en el bloque 3. |
| S4-T02 | HU34 | Estado mostrado real, sin prometer cobertura fuera del entorno validado | Pendiente | — |
| S4-T03 | HU35 | Envío de la posición cada 15 s con hora y precisión | Implementada (servidor) | `POST /api/acompanamiento/{id}/posiciones`: solo del titular y con la sesión activa y no vencida según el reloj del servidor; guarda la hora de obtención (una posición antigua conserva la suya) y la precisión; un reenvío no la duplica. La sesión informa el intervalo de 15 s (`Acompanamiento:IntervaloPosicionesSegundos`). `AcompanamientoTests.HU35_*`. |
| S4-T04 | HU35 | Estados reales de la transmisión según SPIKE01; reestimación de HU35 si procede | Pendiente | — |
| S4-T05 | HU36 | Finalización por el ciudadano: detención local inmediata, cierre en el servidor y cierre sin conexión | Implementada (servidor) | `POST /api/acompanamiento/{id}/finalizacion`: «Finalizada» con la hora de cierre; un reenvío o varias a la vez dan el mismo estado final; no cambia ningún SOS ni crea casos. `AcompanamientoTests.HU36_*`. La detención local y el cierre sin conexión son de la app (bloque 3). |
| S4-T06 | HU36 | Rechazo de posiciones posteriores al cierre (regla de cierre compartida) | Implementada | Regla única `SesionAcompanamiento.AdmitePosiciones` (activa y antes del vencimiento, con el reloj del servidor); la finalización bloquea la sesión antes de tomar la hora de cierre, así que ninguna posición incorporada es posterior al cierre (prueba con 20 posiciones simultáneas). 409 tras finalizar o vencer. `SesionAcompanamientoTests`, `AcompanamientoTests.HU35_CA02_*`, `HU36_CA04_*`, `HU37_CA04_*`. |
| S4-T07 | HU37 | Cierre automático a los 30 minutos en el servidor, sin renovación y reflejado al reconectar | Implementada (servidor) | Temporizador `VencimientoAcompanamientos` (cada 5 s, `Acompanamiento:IntervaloVencimientoSegundos`) que cierra como «Vencida» con la hora de vencimiento, sin llamadas del dispositivo; las consultas también vencen al acceder; no renueva ni crea sesiones; `GET /api/acompanamiento/{id}` muestra «Vencida» al reconectar. `AcompanamientoTests.HU37_*`. |
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
| 2 — Acompañamiento en el servidor (HU34–HU37) | 16/10/2026 | S4-T01, S4-T03, S4-T05–S4-T07 (servidor); S4-T17 (parcial) | `dotnet test`: servidor **687/687** (65 nuevas, incluidas las cinco rutas en la matriz: 25 casos). Release: 0 advertencias. 15 mutaciones detectadas: posiciones aceptadas tras el vencimiento o tras el cierre, duración de 60 minutos, sin exigir la finalidad o la ubicación, hora de cierre del temporizador distinta del vencimiento, sin temporizador, finalización tardía registrada como «Finalizada», sesión o posición ajena, sin el bloqueo de la finalización, coordenadas en el registro, rutas abiertas a cualquier sesión, reintento simultáneo rechazado y una sesión vencida sin cerrar que impide iniciar otra (sobrevivía; se detecta con una prueba nueva). Las pruebas encontraron una carrera: la hora de cierre se tomaba antes de esperar a las posiciones en curso, y 6 de 20 posiciones quedaban después del cierre; corregido con un bloqueo de la sesión antes de tomar la hora. Verificación contra el servidor local por la API (cuenta ciudadana de prueba): sin finalidad o sin ubicación 400; inicio 201 «Activa» de 30 minutos con intervalo de 15 s; reintento 200; segunda sesión 409; tres posiciones 201; finalización 200 «Finalizada» y repetida 200; posición tardía 409; sin sesión activa 204. Una sesión llevada a su vencimiento en la base local pasó a «Vencida» (cierre = vencimiento) a los 7 s sin ninguna llamada del dispositivo, y la app la ve «Vencida» al reconectar. El registro del servidor no contiene coordenadas. |

### Decisiones y observaciones del bloque 1

- **Dónde:** la desactivación está en la ficha de la cuenta (`/panel/admin/cuentas/{id}/editar`), en una sección «Estado de la cuenta». Se pide una confirmación explícita («Desactivar la cuenta…» → «Confirmar la desactivación»), sin JavaScript (`<details>`), porque la acción impide el acceso de una persona.
- **Qué cambia (CA01, CA02):** solo el estado de la cuenta. El inicio de sesión ya rechazaba las cuentas desactivadas desde HU02 (403 en la app; aviso en el panel), sin revelar el estado a quien no conoce la contraseña.
- **Registros conservados (CA03):** la cuenta no se borra; sus eventos, asignaciones y cierres siguen vinculados y visibles en el historial y en la búsqueda de casos (el filtro de serenos ya incluía las cuentas desactivadas).
- **Solo cuentas institucionales:** una cuenta de ciudadano o de administrador no existe para esta operación (404), igual que en HU04.
- **Repetición y simultaneidad:** desactivar una cuenta ya desactivada no es un error. Si dos administradores la desactivan a la vez, el sello de concurrencia de Identity detecta el conflicto y la nueva lectura encuentra la cuenta ya desactivada.
- **DP03 (CA04, pendiente):** no se aplica ninguna regla. Con el código actual, una sesión ya abierta sigue válida hasta que vence (el JWT de la app dura lo configurado; la cookie del panel, 8 horas) y una asignación vigente no cambia. La página lo advierte al administrador. La propuesta para el PO está en «Decisiones pendientes».
- **Sin reactivación:** HU05 no la pide y HU04 no la incluye entre los datos modificables; si el PO la quiere, es una historia o un criterio nuevo.
- **Registro:** solo «Cuenta institucional {id} desactivada.», sin correo ni nombre.

### Decisiones y observaciones del bloque 2

- **Módulo propio:** `Modulos/Acompanamiento`, separado de los casos (tablas `SesionesAcompanamiento` y `PosicionesAcompanamiento`). Una sesión no es un SOS ni un reporte (HU34-CA05) y tendrá su propio historial (HU39-A).
- **Inicio (HU34):** el servidor registra el inicio y el vencimiento (inicio + 30 minutos, CA01). La app declara que el ciudadano aceptó la finalidad y que hay permiso y posición (`FinalidadAceptada`, `UbicacionDisponible`), igual que el sereno declara su permiso (DP16-a); sin ellas, 400 y no se crea nada (CA02, CA03). El servidor no puede comprobar el permiso del teléfono: la comprobación real es de la app (bloque 3).
- **Una sola sesión (CA06):** índice único parcial (una «Activa» por ciudadano) más el `IdSolicitud` de cada pulsación. Una segunda sesión responde 409; un reintento de la misma pulsación devuelve la misma sesión. Una sesión vencida que el temporizador aún no cerró se cierra antes de evaluar un inicio, así que no lo impide.
- **Estado persistente (CA08):** `GET /api/acompanamiento/actual` devuelve la sesión activa con la hora del servidor (`Ahora`), para que la app calcule el tiempo restante sin depender de su reloj.
- **Posiciones (HU35):** se guardan con su hora de obtención y su precisión, y la hora de recepción del servidor. Una posición antigua conserva su hora (CA03): la central podrá ver su antigüedad (HU38). Una hora de obtención más de 5 minutos en el futuro se rechaza (el margen técnico de DP08). Un reenvío de la misma posición no la duplica.
- **Regla de cierre compartida (S4-T06):** `AdmitePosiciones` exige la sesión activa y antes de su vencimiento según el reloj del servidor. Lo que llega después se rechaza con 409 (HU35-CA02, HU36-CA04, HU37-CA04), también si el temporizador aún no la cerró.
- **Carrera encontrada por las pruebas:** la finalización tomaba la hora de cierre antes de esperar a las posiciones que se estaban guardando. Quedaban posiciones con hora de recepción posterior al cierre (6 de 20 en la prueba). Ahora la posición se guarda con un bloqueo compartido de la sesión y la finalización toma un bloqueo exclusivo antes de leer la hora.
- **Finalización (HU36):** registra «Finalizada» y la hora (CA02). Un reenvío o varias simultáneas devuelven el mismo estado final (CA07). Si llega después del vencimiento (por ejemplo, guardada sin conexión), la sesión queda «Vencida»: no se reescribe como finalizada.
- **Vencimiento (HU37):** un temporizador del servidor (cada 5 s, Plan de Arquitectura 9.9) cierra las sesiones vencidas como «Vencida», con la hora de vencimiento como hora de cierre, aunque el teléfono no vuelva a llamar (CA01). No renueva ni crea sesiones (CA03). Al reconectar, `GET /api/acompanamiento/{id}` muestra cómo terminó (CA06).
- **Sin peligro ni cancelación (HU36-CA05/CA06, HU37-CA05):** cerrar una sesión no toca ningún caso ni crea un SOS, un aviso o un despacho. Los avisos del acompañamiento son de HU42 (Sprint 5).
- **Registros (RNF07):** solo el identificador de la sesión y el evento (iniciado, finalizado, vencido); nunca coordenadas ni precisión.
- **Pendiente para la app (bloque 3):** la aceptación de la finalidad, la comprobación del permiso y de la posición, el envío cada 15 s, la detención local inmediata y el cierre sin conexión (DP20).
