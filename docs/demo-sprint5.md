# Demostración del Sprint 5 — cancelación, evaluación, tiempos, mapa de concentración, bienestar y avisos (S5-T17)

Guía para la revisión del Sprint 5. Recorre la cancelación de un SOS pendiente, la evaluación de un caso cerrado y su consulta,
los tiempos por caso, el mapa de concentración y, durante un acompañamiento, la confirmación de bienestar, los avisos del
servidor y su atención con un caso urgente. Todos los datos son ficticios.

Cada paso se verificó de extremo a extremo el 16 y el 17/10/2026 contra el servidor local, con el panel en Chrome (Playwright)
y la app en el emulador Android 15 (`cad_pixel`). Los resultados de cada bloque están en
[trazabilidad-sprint5.md](trazabilidad-sprint5.md) y las capturas son de esas verificaciones.

## Preparación

- Servidor con las migraciones del Sprint 5 aplicadas (`CancelacionSos`, `EvaluacionCaso`, `ConfirmacionBienestar` y
  `AvisosAcompanamiento`).
- Cuentas ficticias: una ciudadana, un operador, un sereno y el administrador.
- Teléfono o emulador con la app nueva y la sesión de la ciudadana. Permisos de ubicación «mientras se usa la app» y de notificaciones.
- El operador abre `/panel/central` y, en otra pestaña, `/panel/acompanamientos`.

## Recorrido

| # | Quién | Qué hacer | Qué se ve | HU |
|---|---|---|---|---|
| 1 | Ciudadana | Pulsar SOS. Después, «Mis casos» → el SOS → «Cancelar este SOS» y «Confirmar la cancelación» sin motivo. | «Indica el motivo de la cancelación.»; no se envía. | HU09-CA04 |
| 2 | Ciudadana | Escribir el motivo y confirmar. | «SOS · Cancelado» con el motivo y «Cancelaste el SOS. La central fue avisada.». | HU09-CA01 |
| 3 | Operador | (En la central, sin recargar.) | El SOS sale de la sección urgente y aparece «Ana Ficticia canceló su SOS» con enlace al historial; el historial lo marca «Cancelada» con el motivo. | HU09-CA02, HU27-CA02 |
| 4 | Operador y sereno | Asignar otro SOS de la ciudadana, que el sereno lo atienda y lo cierre. | El SOS «Cerrado» en «Mis casos». Un SOS asignado ya no ofrece cancelar. | HU09-CA03, HU24 |
| 5 | Ciudadana | En el caso cerrado, «Evaluar la atención (opcional)»: enviar sin puntuación y después con 4 y un comentario. | Sin puntuación no se envía; después, «Tu evaluación: 4 de 5». No se ofrece otra evaluación. | HU32 |
| 6 | Operador | Historial de ese caso. | «Evaluación del ciudadano: 4 de 5» con el comentario; en un caso cerrado sin evaluar, ninguna puntuación. | HU33 |
| 7 | Operador | «Casos» con el estado «Cerrada». | Columnas «Hasta la decisión» y «Hasta el inicio de atención» (en un SOS, tiempo de respuesta); «No corresponde» y «No disponible» donde toca. En el detalle del SOS, «Tiempos del caso». | HU28, DP18 |
| 8 | Operador | «Mapa de concentración»: los últimos 30 días; después «Solo reportes». | Cuadros rojos (SOS) y círculos violetas (reportes) con el número de casos por zona; totales por origen y cuántos no tienen ubicación. Sin cancelados ni descartados. | HU29, HU17-CA04 |
| 9 | Ciudadana | «Acompañamiento» → iniciarlo. | Acompañamiento activo (Sprint 4). | HU34 |
| 10 | Operador | En el acompañamiento, «Solicitar confirmación». | «Pendiente: aún no se mostró al ciudadano»; no se puede pedir otra mientras espera. | HU40-CA01, CA04, DP20 |
| 11 | Ciudadana | (En la app, en el siguiente envío.) | «¿Cómo estás?» con «Estoy bien» y «Necesito ayuda»; el panel pasa a «mostrada al ciudadano». | HU41-CA01 |
| 12 | Ciudadana | «Estoy bien». | La central lo ve y la sesión sigue activa. | HU41-CA02 |
| 13 | Ciudadana y operador | Con la app en segundo plano, el operador pide otra confirmación. | Notificación «La central pregunta cómo estás»; la presentación no se registra hasta abrir la pantalla. | HU41-CA01 |
| 14 | Ciudadana | «Necesito ayuda». | «Pedido de ayuda registrado: la central recibió tu SOS.»: se vincula con su SOS vigente o se emite uno con su ubicación (aparece en la sección urgente). | HU41-CA03, CA04 |
| 15 | Operador | Pedir otra confirmación; la ciudadana no responde y activa el modo avión. | Sin intervención: «Ubicación desactualizada» a los 60 s y «Confirmación sin respuesta» a los 2 min, en «Acompañamientos» (no en la sección urgente) y sin repetirse. | HU42, DP12 |
| 16 | Operador | Abrir un aviso: «Revisar el aviso», registrar un contacto, «Crear caso urgente» sin justificación y con ella. | En revisión con su responsable; la actuación con hora; sin justificación no se crea; con ella, «Caso urgente · Acompañamiento» pendiente en la central, sin asignar. | HU43-CA01, CA02, CA05, CA06, DP19 |
| 17 | Operador | El otro aviso: vincularlo con ese caso y resolver los dos. | Vinculado; «Resuelto» conservando responsable y actuaciones. El caso urgente se asigna con HU20. | HU43-CA03, CA04 |

## Capturas de las verificaciones (16 y 17/10/2026)

App:
[SOS pendiente](capturas/movil/72-hu09-sos-pendiente.png),
[cancelar sin motivo](capturas/movil/73-hu09-sin-motivo.png),
[con motivo](capturas/movil/74-hu09-motivo.png),
[cancelado](capturas/movil/75-hu09-cancelado.png),
[evaluar](capturas/movil/76-hu32-evaluar.png),
[sin puntuación](capturas/movil/77-hu32-sin-puntuacion.png),
[formulario](capturas/movil/78-hu32-formulario.png),
[evaluado](capturas/movil/79-hu32-evaluado.png),
[confirmación](capturas/movil/80-hu41-confirmacion.png),
[«Estoy bien»](capturas/movil/81-hu41-estoy-bien.png),
[notificación en segundo plano](capturas/movil/82-hu41-notificacion.png),
[«Necesito ayuda»](capturas/movil/83-hu41-necesito-ayuda.png).

Panel:
[cancelación en la central](capturas/panel/hu09-central-cancelacion.png),
[historial «Cancelada»](capturas/panel/hu09-historial-cancelada.png),
[evaluación en el historial](capturas/panel/hu33-evaluacion-historial.png),
[tiempos en «Casos»](capturas/panel/hu28-busqueda-tiempos.png),
[tiempo de respuesta](capturas/panel/hu28-tiempo-respuesta-sos.png),
[tiempos de un reporte](capturas/panel/hu28-tiempos-reporte.png),
[mapa de concentración](capturas/panel/hu29-mapa-concentracion.png),
[solo reportes](capturas/panel/hu29-solo-reportes.png),
[confirmación solicitada](capturas/panel/hu40-solicitada.png),
[«Estoy bien»](capturas/panel/hu40-estoy-bien.png),
[«Necesito ayuda» con su SOS](capturas/panel/hu41-necesito-ayuda-central.png),
[avisos](capturas/panel/hu42-avisos-lista.png),
[caso urgente desde un aviso](capturas/panel/hu43-aviso-caso-urgente.png),
[caso urgente en la central](capturas/panel/hu43-caso-urgente-central.png),
[aviso vinculado](capturas/panel/hu43-aviso-verificacion-vinculado.png).

## Entorno publicado

Desde el desarrollo no se despliega ni se entra a Render. Al fusionar los PR del Sprint 5 en `main` con la CI en verde, Render
despliega y aplica las cuatro migraciones al arrancar. El recorrido en el entorno publicado lo hace el equipo en la Sprint
Review, con el APK nuevo en un teléfono real. Ahí hay que comprobar la notificación de la confirmación con la app en segundo
plano y el ahorro de batería de cada fabricante, como en S3-T20.
