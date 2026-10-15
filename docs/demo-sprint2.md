# Demostración del Sprint 2 — ciclo completo del SOS (S2-T17)

Guía para la revisión del Sprint 2. Recorre el ciclo de un SOS desde que llega a la central hasta su cierre, con los tres
roles: ciudadano, operador y sereno. Todos los datos son ficticios.

Verificada de extremo a extremo el 14/10/2026 contra el servidor local: operador en Chrome y sereno en la app del emulador
Android 15. El SOS de la ciudadana se emitió por la API, porque el emulador tenía abierta la sesión del sereno; la emisión
desde la app ya se verificó en el Sprint 1 (HU07, HU08).

## Preparación

- Servidor con las migraciones del Sprint 2 aplicadas (local o el entorno publicado).
- Cuentas ficticias: una ciudadana, un operador y un sereno (el administrador crea las dos últimas en `/panel/admin/cuentas`, HU04).
- Teléfono o emulador con la app y la sesión del sereno; otro dispositivo (o la API) para la ciudadana.
- El operador abre `/panel/central` y comprueba «Conectado en tiempo real».
- El sereno abre la app y activa su estado operativo (HU18): debe aparecer «Disponible» en la sección «Serenos» de la central (HU19).

## Recorrido

| # | Quién | Qué hacer | Qué se ve | HU |
|---|---|---|---|---|
| 1 | Ciudadana | Emite un SOS con ubicación. | El SOS aparece en «Atención urgente» sin recargar, con «Tipo por determinar» y su marcador en el mapa. | HU07, HU08, HU11, HU13-CA03 |
| 2 | Operador | «Verificar y registrar contacto»: resultado «Contactado», información del hecho y referencia indicada por la ciudadana. | El intento queda en el historial; la referencia se guarda aparte de las coordenadas. | HU12 |
| 3 | Operador | En «Clasificación», elige una categoría del catálogo. | El tipo, el operador y la hora; la central muestra «Tipo: …». | HU13 |
| 4 | Operador | En la central: «Elegir sereno para asignar» → «Asignar al SOS seleccionado» en un sereno disponible → «Confirmar asignación». | El SOS sale de «Pendientes» y el sereno pasa a «No disponible · con asignación vigente». | HU19, HU20 |
| 5 | Sereno | (Sin tocar la app.) | «Tienes un nuevo despacho» con origen, estado, ubicación, información y referencia; sin nombre ni teléfono de la ciudadana. | HU21 |
| 6 | Sereno | «Voy en camino». | La central muestra «Caso En camino» en la ficha del sereno y el avance en el detalle. | HU22 |
| 7 | Sereno (u operador) | «Iniciar atención» (el operador puede hacerlo desde el detalle del SOS). | «En atención» en la app y en la central, con su responsable. | HU23 |
| 8 | Sereno (u operador) | «Cerrar el caso»: tipificación final, resultado y notas internas. Probar antes a confirmar sin datos. | Sin datos: errores en los campos y el caso sigue «En atención». Con datos: «Caso cerrado». | HU24 |
| 9 | Operador | Abrir el detalle del SOS cerrado y volver a la central. | Toda la trazabilidad: intento, clasificación, asignación (finalizada), avances y cierre con resultado público y notas internas separados. El sereno vuelve a «Disponible». | HU24-CA04, CA07 |

## Capturas de la verificación

1. [Central con el SOS sin tipo](capturas/demo-sprint2/01-central-sos.png)
2. [Detalle verificado y clasificado](capturas/demo-sprint2/02-detalle-verificado-clasificado.png)
3. [Confirmación de la asignación](capturas/demo-sprint2/03-confirmar-asignacion.png)
4. [Despacho en la app](capturas/demo-sprint2/04-app-despacho.png)
5. [En atención en la app](capturas/demo-sprint2/05-app-en-atencion.png)
6. [Formulario de cierre](capturas/demo-sprint2/06-app-formulario-cierre.png)
7. [Caso cerrado en la app](capturas/demo-sprint2/07-app-cerrado.png)
8. [Detalle final en la central](capturas/demo-sprint2/08-detalle-final.png)

## Qué no se muestra todavía

- **Notificación push del despacho (ADR-07):** la app ya pide el permiso y registra el dispositivo en el proyecto de
  Firebase del equipo (bloque 7b). La credencial de servidor `Push__CredencialFcm` ya está configurada en Render; tras el
  despliegue, comprobar en la demo que la notificación llega con la app cerrada. Con la app abierta, el aviso llega además por
  el canal en tiempo real.
- **Vista del ciudadano del resultado (HU30, Sprint 3):** el resultado público ya se guarda separado de las notas internas.
- **Asignación de reportes admitidos (HU20-CA07, Sprint 3).**
- **Medición formal de RNF02 en el entorno publicado (S1-T18 / S2-T15).**
