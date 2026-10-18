# Demostración del Sprint 3 — reportes, revisión y seguimiento ciudadano (S3-T18)

Guía para la revisión del Sprint 3. Recorre un reporte de incidencia desde que la ciudadana lo envía con la app hasta que
lo cierra el sereno y la ciudadana ve el resultado, con las decisiones del operador (revisión, corrección de categoría,
admisión, derivación y descarte), la búsqueda de casos y el historial. Todos los datos son ficticios.

Verificada de extremo a extremo el 16/10/2026 contra el servidor local: operador en Chrome (Playwright) y ciudadana y
sereno en la app del emulador Android 15 (`cad_pixel`). Los dos reportes que se derivan y se descartan se crearon por la
API con la cuenta de la ciudadana, para acortar el recorrido; el formulario de la app ya se verificó en el bloque 3.

## Preparación

- Servidor con las migraciones del Sprint 3 aplicadas (local o el entorno publicado).
- Cuentas ficticias: una ciudadana, un operador y un sereno (el administrador crea las dos últimas, HU04).
- El sereno abre la app y activa su estado operativo (HU18): debe figurar «Disponible» en la central.
- El operador abre `/panel/central` y comprueba «Conectado en tiempo real».
- En el entorno publicado, la app debe apuntar a `https://cad-servidor.onrender.com/` (campo «Dirección del servidor» de la pantalla de ingreso).

## Recorrido

| # | Quién | Qué hacer | Qué se ve | HU |
|---|---|---|---|---|
| 1 | Ciudadana | Inicio → «Reportar una incidencia». Leer la advertencia de SOS. Tipo «Otros», descripción, «Marcar en el mapa», tocar el lugar y escribir una referencia. «Enviar reporte». | «Reporte registrado a las … (estado: Recibido)». La posición del teléfono no se usa: el punto es el elegido en el mapa. | HU10 |
| 2 | Operador | «Reportes»: abrir el reporte nuevo. | Descripción, fecha aproximada y lugar del hecho con su mapa y referencia. No aparece en «Atención urgente · SOS». | HU10-CA06, HU14 |
| 3 | Operador | «Iniciar la revisión»; en «Categoría», corregir a «Robo». | Responsable y hora de la revisión; la original («Otros») y la vigente («Robo»). | HU14 |
| 4 | Operador | «Decisión»: «Admitir para atención» con un motivo. | «Pendiente»: aparece en «Reportes admitidos» de la central con su marcador violeta. | HU15 |
| 5 | Operador | En otro reporte: «Derivar» sin destino (se rechaza) y después con destino, acción y motivo. En un tercero: «Descartar» con motivo. | Derivación incompleta: errores por campo y el estado no cambia. «Derivada» y «Descartada» son estados finales; el descartado no aparece en la central. | HU16, HU17 |
| 6 | Operador | Central: elegir el reporte admitido → «Asignar al caso seleccionado» en el sereno disponible → «Confirmar asignación». | El reporte sale de «Reportes admitidos» y el sereno pasa a «No disponible · con asignación vigente». | HU20-CA07 |
| 7 | Sereno | (Sin tocar la app.) | «REPORTE · Asignada» con el lugar del hecho indicado por el ciudadano y su referencia; sin la descripción ni los datos de la ciudadana (DP06). | HU21 |
| 8 | Sereno | «Voy en camino», «Iniciar atención» y «Cerrar el caso» con tipificación y resultado. | «Caso cerrado a las …»; el sereno vuelve a estar disponible. | HU22–HU24 |
| 9 | Ciudadana | Volver al inicio de la app. | «Notificaciones (N sin leer)» y el aviso de notificaciones nuevas. | HU31 |
| 10 | Ciudadana | Abrir «Notificaciones» y tocar «Tu reporte pasó a «Cerrado».». | La bandeja solo dice el origen y el nuevo estado. Al tocarla se abre el caso en «Mis casos» y queda leída. | HU31 |
| 11 | Ciudadana | Ver el caso (y «Mis casos»). | Resultado público y avances Recibido → En revisión → Admitido → En atención → Cerrado, sin operador, sereno ni notas internas. El derivado muestra su destino y el descartado su motivo. | HU30 |
| 12 | Operador | «Casos»: filtrar por origen «Reporte» (y combinar con estado o fechas); abrir «Historial» del reporte cerrado. | Resultados con todos los filtros cumplidos, los más recientes primero. Historial completo, en orden, con responsable y hora, y el resultado final arriba. | HU26, HU27 |

## Resultados de la verificación (16/10/2026)

- **App (ciudadana):** reporte con punto en el mapa y referencia, registrado «Recibido».
- **Panel:** revisión iniciada con el operador como responsable; categoría corregida de «Otros» a «Robo» conservando la original; admisión, derivación y descarte registrados; el admitido aparece en la central (1) y el descartado no (0); asignación al sereno de prueba. 0 errores de consola.
- **App (sereno):** despacho «REPORTE · Asignada» con el lugar del hecho y la referencia, sin ubicación del dispositivo; en camino, atención y cierre.
- **App (ciudadana):** 8 notificaciones nuevas de la demostración (revisión, admisión, «En atención» y cierre del reporte de la app; revisión y decisión de los otros dos), con textos sin datos del hecho; la del cierre abre el caso con su resultado y los cinco avances públicos.
- **Historial:** 9 actuaciones en orden, del registro (ciudadana) al cierre (sereno), con la corrección «Otros» → «Robo».
- **Hora en la app:** el emulador usa la zona horaria UTC, así que la app muestra 21:16 donde el panel (hora de Lima) muestra 16:16. Es la configuración del emulador; un teléfono en Perú muestra la hora local.
- **Base local:** la central tiene además 111 SOS pendientes del ensayo de carga de RNF02; no afectan el recorrido, pero por eso las capturas de la central no se incluyen (la asignación se verificó con el despacho del sereno). En el entorno publicado la central está limpia.

## Capturas

1. [Reporte con el punto en el mapa](capturas/demo-sprint3/01-app-reporte-mapa.png)
2. [Reporte registrado](capturas/demo-sprint3/02-app-reporte-recibido.png)
3. [Bandeja «Reportes»](capturas/demo-sprint3/03-reportes.png)
4. [Revisión y corrección de categoría](capturas/demo-sprint3/04-revision-y-correccion.png)
5. [Reporte derivado](capturas/demo-sprint3/05-derivado.png)
6. [Despacho del reporte en la app del sereno](capturas/demo-sprint3/06-app-sereno-despacho-reporte.png)
7. [Cierre en la app del sereno](capturas/demo-sprint3/07-app-sereno-reporte-cerrado.png)
8. [Inicio de la ciudadana con notificaciones](capturas/demo-sprint3/08-app-inicio-notificaciones.png)
9. [Bandeja de notificaciones](capturas/demo-sprint3/09-app-bandeja.png)
10. [Caso cerrado en «Mis casos»](capturas/demo-sprint3/10-app-mi-caso-cerrado.png)
11. [Casos filtrados por origen](capturas/demo-sprint3/11-casos-reportes.png)
12. [Historial del reporte cerrado](capturas/demo-sprint3/12-historial-reporte.png)

## Entorno publicado

El PR #25 se fusionó en `main` el 16/10/2026 y la CI de `main` pasó (servidor y app), lo que dispara el despliegue en
Render (`autoDeployTrigger: checksPass`). Desde el desarrollo solo se consultó `/salud` (responde `Healthy`); la
comprobación del recorrido en el entorno publicado, con las migraciones `DecisionesReporte` y `NotificacionesCiudadano`
(`/salud/lista`), corresponde al equipo en la Sprint Review.

## Pendiente para el equipo (trasladado)

- **S3-T19:** medición formal de RNF02 en Render ([pruebas.md](pruebas.md)).
- **S3-T20:** SPIKE01 en un teléfono Android real (incluye HU06, HU07, HU08 y HU18).
- **S3-T21:** entrega real de la notificación push del despacho con la app cerrada (la credencial ya está en Render).
- **APK nuevo** para el teléfono del equipo con la versión del Sprint 3 ([app-movil.md](app-movil.md)).
