# Demostración del Sprint 4 — acompañamiento temporal, supervisión, mapa de casos y cuentas (S4-T19)

Guía para la revisión del Sprint 4. Recorre el acompañamiento de una ciudadana desde la app (activación, envío de su
posición, pérdida de conexión, finalización y vencimiento), su supervisión en el panel, la conservación de su recorrido,
el mapa de casos y la desactivación de una cuenta institucional. Todos los datos son ficticios.

Cada paso se verificó de extremo a extremo el 16/10/2026 contra el servidor local, con el panel en Chrome (Playwright) y la
app en el emulador Android 15 (`cad_pixel`); los resultados de cada bloque están en
[trazabilidad-sprint4.md](trazabilidad-sprint4.md). Las capturas son de esas verificaciones.

## Preparación

- Servidor con las migraciones del Sprint 4 aplicadas (`Acompanamiento`, `AsignacionCancelada`,
  `UltimoContactoAcompanamiento` y `ConservacionRecorridos`).
- Cuentas ficticias: una ciudadana, un operador, un sereno y el administrador; un segundo sereno ficticio para la desactivación.
- Teléfono o emulador con la app y la sesión de la ciudadana; permiso de ubicación «mientras se usa la app» y notificaciones.
- El operador abre `/panel/central` y, en otra pestaña, `/panel/acompanamientos`.

## Recorrido

| # | Quién | Qué hacer | Qué se ve | HU |
|---|---|---|---|---|
| 1 | Ciudadana | Inicio → «Acompañamiento». Pulsar «Iniciar acompañamiento» sin marcar la finalidad. | «Para iniciar, marca que aceptas…»; no se llama al servidor. | HU34-CA02 |
| 2 | Ciudadana | Marcar «Acepto compartir mi ubicación…» e iniciar. | «Acompañamiento activo», 30 minutos, «Última posición enviada a las …» y la notificación permanente «Acompañamiento activo». El inicio muestra «Quedan N min» y no deja salir. | HU34, HU35, DP13 |
| 3 | Operador | `/panel/acompanamientos`. | La sesión «Activa», «Posición actual» con su hora, precisión y una antigüedad que avanza; marcador verde «A» en su mapa. | HU38-CA01, CA02, CA04 |
| 4 | Ciudadana | Bloquear el teléfono o cerrar la app desde recientes durante un minuto. | La central sigue recibiendo posiciones cada 15 s. | DP13 |
| 5 | Ciudadana | Activar el modo avión. | En la app: «Sin conexión con el servidor…». En el panel, sin recargar y pasados 60 s: «Sin conexión», «Última posición conocida» y la advertencia de posición desactualizada; marcador gris. | HU35-CA04, HU38-CA03, DP12, DP20 |
| 6 | Ciudadana | Con el modo avión, «Finalizar acompañamiento». | Se deja de transmitir al instante; «el cierre se enviará en cuanto vuelva la conexión». El panel la sigue mostrando «Sin conexión». | HU36-CA01, CA03, DP20 |
| 7 | Ciudadana | Quitar el modo avión. | «Acompañamiento finalizado»; desaparece la notificación. El panel ya no la muestra entre las activas. | HU36-CA02 |
| 8 | Operador | «Historial de sesiones» y abrir la sesión. | Inicio, vencimiento, cierre («Finalizada por el ciudadano») y el recorrido, sin ninguna posición «actual». | HU39-A |
| 9 | Operador | En la sesión, «Conservación del recorrido»: el identificador de un caso y la finalidad. | Queda registrada con responsable y hora; esas posiciones no se eliminarán a los 30 días. | HU39-B-CA05, DP22 |
| 10 | — | (Vencimiento) Una sesión que llega a los 30 minutos. | El servidor la cierra como «Vencida» sin el teléfono; la app muestra «terminó a los 30 minutos». | HU37 |
| 11 | Operador | `/panel/central`: elegir un marcador con borde. | Su identificador, origen y estado y «Ver el caso»; los pendientes (relleno) se asignan desde el mapa. Un reporte nuevo aparece sin recargar. | HU25 |
| 12 | Administrador | Desactivar la cuenta de un sereno con un caso en curso. | La app del sereno muestra «Tu sesión terminó»; el caso vuelve a «Pendiente» y su historial registra «Asignación cancelada». | HU05, DP03 |
| 13 | Operador | Un reporte en curso: «Registrar inicio de atención» y cerrarlo. | El cierre por el operador, como en un SOS; el sereno había recibido la descripción del reporte. | HU23, HU24, DP06 |

## Capturas de las verificaciones (16/10/2026)

App:
[antes de iniciar](capturas/movil/61-acompanamiento-antes-de-iniciar.png),
[sin finalidad](capturas/movil/62-acompanamiento-sin-finalidad.png),
[activo](capturas/movil/63-acompanamiento-activo.png),
[inicio con el acompañamiento](capturas/movil/64-inicio-acompanamiento-activo.png),
[notificación](capturas/movil/71-acompanamiento-notificacion.png),
[sin conexión](capturas/movil/65-acompanamiento-sin-conexion.png),
[cierre pendiente](capturas/movil/66-acompanamiento-cierre-pendiente.png),
[finalizado](capturas/movil/67-acompanamiento-finalizado.png),
[vencido](capturas/movil/68-acompanamiento-vencido.png),
[despacho con descripción](capturas/movil/69-sereno-despacho-con-descripcion.png),
[sesión cerrada al desactivar](capturas/movil/70-sereno-desactivado-sesion-cerrada.png).

Panel:
[acompañamiento activo](capturas/panel/hu38-acompanamiento-activo.png),
[sin conexión](capturas/panel/hu38-acompanamiento-sin-conexion.png),
[historial de sesiones](capturas/panel/hu39-historial-sesiones.png),
[recorrido](capturas/panel/hu39-recorrido-sesion.png),
[conservar](capturas/panel/hu39b-conservar-recorrido.png),
[posiciones eliminadas](capturas/panel/hu39b-posiciones-eliminadas.png),
[recorrido conservado](capturas/panel/hu39b-recorrido-conservado.png),
[mapa de casos](capturas/panel/hu25-mapa-casos.png),
[desactivar con asignación](capturas/panel/s4-desactivar-con-asignacion.png),
[historial con la asignación cancelada](capturas/panel/s4-historial-asignacion-cancelada.png),
[atención de un reporte](capturas/panel/s4-reporte-en-camino-atencion.png),
[reporte cerrado por el operador](capturas/panel/s4-reporte-cerrado-por-operador.png),
[desactivación](capturas/panel/hu05-confirmar-desactivacion.png).

## Entorno publicado

Desde el desarrollo no se despliega ni se entra a Render. Al fusionar los PR del Sprint 4 en `main` con la CI en verde, Render
despliega y aplica las cuatro migraciones al arrancar. El recorrido en el entorno publicado lo hace el equipo en la Sprint
Review, con el APK nuevo en un teléfono real (S3-T20: la transmisión en segundo plano y el ahorro de batería de cada fabricante).
