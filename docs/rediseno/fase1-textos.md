# Fase 1 · Textos del sistema de diseño

Textos cortos para los componentes nuevos (skill `design:ux-copy`). Voz: español formal y claro; **tú** con el ciudadano y el sereno; impersonal y preciso con el operador. Los **nombres de estado no cambian**: los define el dominio (`EstadoCaso` en `Caso.cs`, `EstadosMiCaso` en `MisCasos.cs`) y los verifican las pruebas. Lo que cambia es cómo se presentan.

> Antes de reemplazar un texto en una vista (fases 3 y 4), buscarlo en `tests/` : si una prueba lo verifica, se conserva literal o se actualiza la prueba en el mismo cambio, explicándolo.

## 1. Chips de estado

Cada estado se muestra como chip con **tono + icono + texto**. El tono indica en qué punto del flujo está el caso, no si es «bueno» o «malo».

| Estado (texto literal) | Tono | Icono (Lucide) | Por qué |
|---|---|---|---|
| Pendiente (SOS) | `urgente` | `siren` | Espera acción inmediata de la central |
| Pendiente (reporte admitido) | `advertencia` | `clock` | Espera asignación, no es urgente |
| Recibido | `informacion` | `inbox` | Estado inicial; antes se mostraba en verde, como si fuera un logro |
| En revisión | `informacion` | `search` | La central lo está evaluando |
| Admitido | `informacion` | `check-square` | Aprobado para atención |
| Asignada / Asignado | `progreso` (primario) | `user-check` | Ya tiene sereno |
| En camino | `progreso` | `navigation` | El sereno se desplaza |
| En atención | `progreso` | `shield` | El sereno está en el lugar |
| Cerrada / Cerrado | `exito` | `check-circle-2` | Atendido |
| Derivada / Derivado | `neutro` | `arrow-up-right` | Pasó a otra entidad |
| Descartada / Descartado | `neutro` | `x-circle` | No procede; el motivo se muestra aparte |
| Cancelada | `neutro` | `ban` | El ciudadano lo canceló |

Acompañamiento: **Activa** → `acompanamiento` + `route`; **Finalizada** / **Vencida** → `neutro` + `flag`.
Avisos: **Generado** → `advertencia` + `bell-ring`; **En revisión** → `informacion` + `search`; **Resuelto** → `exito` + `check`.
Sereno: **Operativo · Disponible** → `exito` + `radio`; **Operativo · Ocupado** → `progreso` + `user-check`; **No operativo** → `neutro` + `power-off`.

## 2. Ayudas de la central en una línea

El texto completo actual pasa al tooltip «i» (se abre con hover y con foco).

| Bloque | Hoy (resumen) | Línea visible | Tooltip |
|---|---|---|---|
| SOS pendientes | «SOS pendientes, del más reciente al más antiguo. Se separan de reportes y acompañamientos.» | **Del más reciente al más antiguo.** | Texto actual completo |
| Reportes admitidos | «Pendientes de asignación tras su revisión. No son alertas urgentes…» | **Revisados y pendientes de asignar. No son urgentes.** | Texto actual completo |
| Serenos | «Disponible: operativo y sin asignación vigente. La ubicación solo se comparte…» | **Disponible = operativo y sin caso asignado.** | Texto actual completo |
| Mapa operativo | Párrafo de leyenda de 4 líneas | *(sin texto: leyenda en chips)* `SOS` · `Reporte` · `Acompañamiento` · `Sereno` + «Relleno: sin asignar · Borde: en atención» | Texto actual completo |

## 3. Estados vacíos

Estructura: qué es + por qué está vacío + qué hacer (solo cuando hay algo que hacer).

| Lugar | Texto |
|---|---|
| Central · SOS | **Sin SOS pendientes.** Los nuevos aparecerán aquí al instante, con aviso sonoro si está activado. |
| Central · Reportes admitidos | **Sin reportes por asignar.** Los reportes admitidos en «Reportes» aparecerán aquí. |
| Central · Serenos | **Ningún sereno operativo.** Aparecerán cuando activen su estado en la app. |
| Reportes | **No hay reportes por revisar.** |
| Casos (con filtros) | **Ningún caso coincide con los filtros.** Prueba con otro rango de fechas o [Limpiar filtros]. |
| Acompañamientos activos | **No hay acompañamientos activos.** |
| Ciudadano · Mis casos | **Aún no tienes casos.** Tus SOS y reportes aparecerán aquí con sus avances. |
| Ciudadano · Avisos | **No tienes avisos.** Te avisaremos cuando tu caso avance. |
| Sereno · sin despacho | **Sin despacho asignado.** Te avisaremos en este teléfono, aunque la app esté cerrada. |

## 4. Acción progresiva del sereno

Un solo botón fijo abajo; la etiqueta dice lo que **hará** al pulsarlo.

| Estado del caso | Botón | Texto de apoyo encima |
|---|---|---|
| Asignada | **Voy en camino** | Avisa a la central que te desplazas. |
| En camino | **Llegué: iniciar atención** | Registra la hora de llegada. |
| En atención | **Cerrar el caso** | Indicarás la tipificación y el resultado. |
| Cerrada | *(sin botón)* **Caso cerrado a las 04:48.** | Quedas disponible para un nuevo despacho. |

Interruptor de estado: **Operativo** / **No operativo**, con la línea «Última posición enviada a las 04:46» cuando está activo.

## 5. Diálogos de confirmación

| Acción | Título | Cuerpo | Botones |
|---|---|---|---|
| Asignar | **¿Asignar este SOS a Sara Serena Ficticia?** | SOS de Ana Ficticia · 23:43. Sara recibirá el despacho en su teléfono. | **Asignar a Sara** · Cancelar |
| Desactivar cuenta | **¿Desactivar la cuenta de Sara Serena Ficticia?** | No podrá ingresar. Su despacho vigente volverá a la central para reasignarlo. | **Desactivar cuenta** · Mantener activa |
| Cancelar SOS (ciudadano) | **¿Cancelar tu SOS?** | La central dejará de atenderlo. Indica el motivo. | **Cancelar SOS** · Volver |

## 6. Mensajes temporales (*toasts*)

Confirman y desaparecen en 4 s (los errores no desaparecen solos).

- **Cuenta creada.** Ya puede ingresar con su correo.
- **SOS asignado a Sara Serena Ficticia.**
- **Intento registrado.**
- **Reporte enviado.** La central lo revisará.

## Notas de localización

- Mantener «SOS» siempre en mayúsculas.
- «Sereno/serena» según la persona; en textos genéricos, «sereno».
- Horas en formato 24 h de Lima (`HH:mm` o `HH:mm:ss`), con cifras tabulares.
