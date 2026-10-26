# App móvil CAD (.NET MAUI, Android)

App nativa del ciudadano y del sereno (ADR-01). En el Sprint 1 cubre: registro (HU01), ingreso por rol (HU02), preparación de la ubicación (HU06) y SOS con una única activación y ubicación actual (HU07, HU08). En el Sprint 2 se añade el modo sereno: estado operativo y envío periódico de la ubicación (HU18); los despachos llegan con HU21 (consulta y aviso) y HU22–HU24.

## Estructura

```
CadMovil.slnx                      solución de la app (separada de Cad.slnx: la CI de Linux no tiene la carga de MAUI)
src/Cad.Movil/
  Servicios/Abstracciones.cs       interfaces de plataforma (ubicación, API, sesión) y ResultadoApi
  Servicios/EmisorSos.cs           HU07/HU08: activación única, reintentos con el mismo IdSolicitud, confirmación tras el servidor
  Servicios/PreparacionUbicacion.cs HU06: explica → solicita → comprueba; no tiene acceso a la API
  Servicios/ApiCad.cs              cliente HTTP (respondió / sin respuesta)
  Servicios/ServiciosMaui.cs       implementaciones MAUI: Geolocation, SecureStorage, Preferences, Shell
  VistasModelo/, Vistas/           MVVM sin librerías externas; mismos tokens de color que el panel
  Platforms/Android/               manifiesto (sin copia de seguridad, permisos de ubicación «mientras se usa») y network_security_config
tests/Cad.Movil.Tests/             xUnit (net10.0) con dobles: sin emulador ni red
```

`Cad.Movil` compila para `net10.0-android` (la app) y `net10.0` (solo para las pruebas de su lógica).

## Requisitos y comandos

- Carga de trabajo `maui-android`, JDK 17 (Microsoft OpenJDK) y Android SDK (plataforma 36). Si el SDK no está en la ruta por defecto, añade `-p:AndroidSdkDirectory=… -p:JavaSdkDirectory=…`.

```bash
dotnet test tests/Cad.Movil.Tests/Cad.Movil.Tests.csproj
```

```bash
dotnet build src/Cad.Movil/Cad.Movil.csproj -t:Install -f net10.0-android
```

En el emulador, el servidor local se ve como `http://10.0.2.2:5220/` (dirección por defecto en Debug). Fuera de ese caso la app exige `https://` (RNF09): lo validan `ValidacionDireccion` y `network_security_config.xml`, que solo permite tráfico sin cifrar hacia `10.0.2.2`.

## Decisiones de la app

- **Una activación = un `IdSolicitud`** (DP14): se genera al tocar SOS y se reutiliza en los reintentos automáticos (1 s y 2 s) y en el manual. Tras confirmar, una nueva activación es un SOS nuevo.
- **Confirmación solo con la respuesta del servidor** (HU07-CA03). «Sin respuesta» (red, tiempo agotado, 5xx, 408, 429) permite reintentar; un rechazo (4xx) no se reintenta. Si la sesión ha caducado, se pide volver a ingresar.
- **Ubicación del SOS** (DP11): solo si el permiso ya está concedido (no se vuelve a pedir al emitir), espera máxima de 10 s, siempre una lectura nueva (nunca la última conocida) y se descarta una posición anterior a la activación. Sin posición, el SOS se envía igual.
- **Permiso en Android:** el sistema informa «denegado» también cuando nunca se ha preguntado. La app lo trata como pendiente (muestra la explicación) mientras el sistema permita volver a preguntar; solo orienta a los ajustes cuando ya no lo permite.
- **Sesión:** el JWT se guarda en `SecureStorage` (Android Keystore); la dirección del servidor, en `Preferences`. `allowBackup=false`.

## Verificación de extremo a extremo (13/10/2026)

Emulador Android 15 (`system-images;android-35;google_apis;x86_64`, AVD Pixel) contra el servidor local (`http://10.0.2.2:5220`, base `cad_movil_e2e`) y la central abierta en Chrome por un operador creado desde el panel de administración. Ubicación simulada con `adb emu geo fix`. Todos los datos son ficticios.

| Caso | HU/CA | Resultado |
|---|---|---|
| Registro desde la app e ingreso automático | HU01, HU02 | Cuenta creada (201) y la app entra en el modo ciudadano. La sesión se conserva al reabrir la app. |
| Primera apertura sin permiso decidido | HU06-CA01 | Se muestra la explicación antes del diálogo del sistema ([04](capturas/movil/04-explicacion.png), [05](capturas/movil/05-dialogo-permiso.png)). **Defecto encontrado y corregido** (ver abajo). |
| Permiso concedido | HU06-CA02, CA04 | «Ubicación disponible. Solo se enviará si emites un SOS» ([06](capturas/movil/06-ubicacion-disponible.png)); el servidor no recibe nada al abrir la app. |
| SOS con ubicación | HU07-CA01/CA03, HU08-CA01 | «Enviando…» con el botón deshabilitado ([10a](capturas/movil/10a-sos-en-curso.png)), después «SOS registrado… Con tu ubicación». La central lo muestra sin recargar, con ± 5 m. |
| Toque simple ×2 y doble toque ×2 (experimento controlado) | HU07-CA01 | +1 SOS en cada gesto, según el log del servidor. Los dos toques del doble toque se inyectan en la misma sesión de `adb shell`, mientras el primer envío está en curso. |
| SOS sin permiso de ubicación | HU08-CA02/CA04, HU07-CA06 | No se abre el diálogo del sistema; «SOS registrado… Ubicación no disponible» ([13](capturas/movil/13-sos-sin-permiso.png)). |
| Modo avión durante el SOS | HU07-CA04 | Tras los reintentos, «No se pudo confirmar el SOS…» y el botón «Reintentar el mismo SOS» ([14](capturas/movil/14-sos-no-confirmado.png)); 0 SOS registrados en el servidor. |
| Reintento manual con la red restablecida | HU07-CA07 | «SOS registrado… Ubicación no disponible» ([15](capturas/movil/15-sos-reintentado.png)); +1 SOS. |
| Central al final | HU11 | 9 activaciones confirmadas = 9 SOS pendientes, sin duplicados ([16](capturas/movil/16-central-final.png)). |
| Salir | HU02 | Vuelve a la pantalla de ingreso ([17](capturas/movil/17-ingreso-final.png)). |
| Registros del servidor de la sesión | S1-T20 | 0 coincidencias de coordenadas, tokens (`eyJ`, `Bearer`), correo ni teléfono del ciudadano. |

**Defecto encontrado en el emulador y corregido:** en la primera apertura, la app mostraba «Sin permiso de ubicación» en lugar de la explicación, porque Android informa `Denied` antes de la primera solicitud. `ServicioUbicacionMaui` ahora recuerda si ya se pidió el permiso y consulta `ShouldShowRationale`. Además, sin relación con ese defecto, se quitó el subrayado nativo de los campos (ya tienen marco propio) y la barra de estado usa iconos oscuros sobre el fondo claro.

**Limitaciones de esta verificación:**
- Desactivar la ubicación del sistema con `settings put secure location_mode 0` no impidió la lectura en el emulador (la posición simulada siguió llegando). El SOS sin ubicación se verificó retirando el permiso; el caso «permiso concedido y GPS apagado» queda cubierto solo por las pruebas unitarias y debe repetirse en un teléfono real.
- El doble toque se inyectó con `adb`, no con un dedo; la garantía de fondo es el semáforo de `EmisorSos` (prueba unitaria y prueba de mutación) más el índice único del servidor.
- Emulador ≠ dispositivo: segundo plano, pérdida de cobertura real y la precisión del GPS corresponden a SPIKE01 (S1-T03/T04), en un teléfono Android real.

## Teléfono real por USB y sonda de SPIKE01

1. Activa la depuración USB en el teléfono, conéctalo y comprueba que `adb devices` lo muestra.
2. Expón el servidor local en el teléfono:
   ```bash
   adb reverse tcp:5220 tcp:5220
   ```
3. Instala la app en Debug (`dotnet build src/Cad.Movil/Cad.Movil.csproj -t:Install -f net10.0-android`). En un teléfono real, la dirección por defecto es `http://localhost:5220/`; también puedes poner `https://cad-servidor.onrender.com/`.
4. En la pantalla de ingreso, «Diagnóstico de ubicación (SPIKE01)» → «Iniciar». El botón solo existe en Debug. Recorre las condiciones de la [nota técnica](spike01/nota-tecnica.md) y recupera el registro:
   ```bash
   adb exec-out run-as pe.cadmvp.movil cat files/spike01.csv
   ```

## Modo sereno (HU18, Sprint 2)

- `Servicios/TurnoSereno.cs` (`ServicioTurnoSereno`): consulta el estado operativo en el servidor al abrir el modo sereno y, si ya era operativo, reanuda el envío. Activar exige el permiso de ubicación concedido (DP16-a): si no se ha decidido, se explica antes de pedirlo; si está denegado, se orienta a los ajustes.
- Mientras está operativo envía una posición actual al activarse y otra cada intervalo indicado por el servidor (DP15: 30 s). Cada lectura tiene un límite de 10 s; sin posición o sin conexión se avisa y se intenta en el siguiente intervalo. Si el servidor responde que ya no está operativo, el envío se detiene.
- Desactivar detiene primero el envío en el dispositivo y después lo comunica al servidor (CA02). Mientras está operativo no se puede salir de la sesión.
- Solo con la app abierta y con conexión (SPIKE01); no hay servicio en primer plano.

## Despacho (HU21, Sprint 2)

- `Servicios/Despacho.cs` (`ServicioDespacho`): consulta el despacho vigente al abrir la pantalla del sereno, al recibir el aviso del canal y al reconectarlo. Sin conexión conserva el último despacho conocido y avisa. Salir lo olvida y deja de escuchar.
- `Servicios/CanalSerenoSignalR.cs`: conexión a `/hubs/sereno` con el token en la cabecera `Authorization` y reconexión automática. Solo funciona con la app abierta.
- La pantalla muestra los datos de DP06: origen, estado, horas, identificador, ubicación del dispositivo o «no disponible», y la información y las referencias registradas por la central (rotuladas como referencias del ciudadano, no como ubicación).
- HU22/HU23: «Voy en camino» con el caso «Asignada» e «Iniciar atención» con el caso «En camino». Si el estado ya cambió, se informa y se vuelve a consultar; si un operador registra el inicio, el canal avisa (`DespachoActualizado`) y la app se actualiza.
- HU24: «Cerrar el caso» con el caso «En atención»: tipificación final (catálogo de `GET /api/sereno/categorias`), resultado (público) y notas internas (opcionales). Los errores de cada campo los devuelve el servidor. Al cerrar, el despacho desaparece y se muestra «Caso cerrado a las …».
- Notificación push (ADR-07, bloque 7b): `Platforms/Android/Push.cs` (token de FCM, permiso de notificaciones de Android 13+, canal «despachos» y receptor de mensajes) y `Servicios/Notificaciones.cs` (cuándo pedir el permiso y registrar o quitar el dispositivo en el servidor). La pantalla del sereno explica el aviso antes de pedir el permiso.
- **Configurar Firebase en un equipo:** descargar `google-services.json` del proyecto `cad-validacion` (consola de Firebase → Configuración del proyecto → app Android `pe.cadmvp.movil`) y copiarlo en `src/Cad.Movil/Platforms/Android/google-services.json`, o compilar con `-p:CadGoogleServicesJson=<ruta>`. No se versiona. Sin él, la app funciona sin push. El dispositivo o emulador necesita Google Play Services (imágenes «Google APIs» o «Google Play»).

## Reporte de incidencia (HU10, Sprint 3)

- `Vistas/ReportePagina.xaml` y `VistasModelo/ReporteVistaModelo.cs`: tipo (catálogo de `GET /api/reportes/categorias`), descripción (hasta 2000 caracteres), fecha y hora aproximadas (no futuras) y lugar del hecho. Arriba, la advertencia de usar el SOS ante peligro inmediato y «Ir al SOS».
- Lugar del hecho: «Marcar en el mapa», «Usar mi posición actual» (único momento en que se lee la posición; el permiso se pide solo entonces) o «Escribir una referencia» (sin posición). El mapa es `Resources/Raw/mapa/reporte.html` con Leaflet 1.9.4 incluido (`leaflet.js`, `leaflet.css` y su licencia) y teselas de OpenStreetMap; un toque se comunica a la app con una navegación `cadmapa://punto?lat=…&lon=…` que se cancela.
- Fotos (DP08 ampliada, [ADR-09](decisiones/ADR-09-fotos-del-reporte.md)): «Tomar una foto» y «Elegir de la galería», hasta tres, con miniatura y «Quitar». `Platforms/Android/Fotos.cs` (`FotosAndroid`) las vuelve a codificar como JPEG de hasta 1600 px y 1 MB, sin EXIF ni ubicación GPS; viajan en la misma solicitud del reporte ([captura](capturas/movil/84-reporte-tres-fotos.png), [registrado](capturas/movil/85-reporte-con-fotos-registrado.png)).
- `Servicios/Reportes.cs` (`RegistradorReporte`): comprueba los datos, envía con un `IdSolicitud` y lo reutiliza en los reintentos (automáticos y «Reintentar el mismo reporte»), como el SOS. Confirma solo con la respuesta del servidor.

## Reporte admitido en el despacho (HU20-CA07, Sprint 3)

- Un reporte admitido y asignado llega al sereno con `LugarHecho`: el punto del hecho, su precisión si el ciudadano usó su posición, y la referencia. No trae ubicación del dispositivo. La pantalla lo muestra como «Lugar del hecho (indicado por el ciudadano)» y oculta la sección «Ubicación del dispositivo». El resto del flujo (en camino, inicio de atención y cierre) es el de un SOS.
- Siguiendo DP06, el sereno no recibe los datos personales del ciudadano. Desde la decisión del PO del 16/10/2026 sí recibe la descripción del reporte, que la app muestra como «Descripción del ciudadano» ([captura](capturas/movil/69-sereno-despacho-con-descripcion.png)).
- DP03 (16/10/2026): si el administrador desactiva la cuenta del sereno, su sesión deja de valer y la app muestra «Tu sesión terminó» en la siguiente acción ([captura](capturas/movil/70-sereno-desactivado-sesion-cerrada.png)).

## Mis casos (HU30, Sprint 3)

- `Vistas/MisCasosPagina.xaml` y `Vistas/MiCasoPagina.xaml` con `VistasModelo/MisCasosVistaModelo.cs`: la lista de SOS y reportes propios (origen, estado público, fecha y resultado) y el detalle con los avances públicos. Se abre desde «Mis casos» en el inicio del ciudadano.
- Solo datos públicos (DP05): nunca operador, sereno, notas internas ni intentos de contacto. Se vuelve a consultar al abrir cada pantalla y al deslizar hacia abajo.

## Cancelar un SOS pendiente (HU09, Sprint 5)

- En el detalle de «Mis casos», un SOS propio en estado «Recibido» (pendiente para la central) muestra «Cancelar este SOS». El formulario pide el motivo (obligatorio, hasta 500 caracteres) y se confirma con «Confirmar la cancelación»; «No cancelar» lo cierra sin enviar.
- Con la respuesta del servidor, el caso pasa a «Cancelado» con el motivo como resultado y el aviso «Cancelaste el SOS. La central fue avisada.». Si la central ya lo asignó, se muestra el rechazo y el estado actual (CA03). Sin respuesta, el motivo se conserva para reintentar; el servidor registra una sola cancelación (CA08). No afecta a un acompañamiento activo (CA07).
- Capturas: [SOS pendiente](capturas/movil/72-hu09-sos-pendiente.png), [sin motivo](capturas/movil/73-hu09-sin-motivo.png), [con motivo](capturas/movil/74-hu09-motivo.png) y [cancelado](capturas/movil/75-hu09-cancelado.png); en la central, [aviso](capturas/panel/hu09-central-cancelacion.png) e [historial](capturas/panel/hu09-historial-cancelada.png).

## Evaluar un caso cerrado (HU32, Sprint 5)

- En el detalle de «Mis casos», un caso propio «Cerrado» sin evaluar muestra «Evaluar la atención (opcional)»: cinco botones de puntuación (1 a 5, con su descripción para lectores de pantalla), un comentario opcional de hasta 2000 caracteres y «Enviar evaluación». Sin puntuación no se envía (CA02).
- Registrada la evaluación, el detalle muestra «Tu evaluación: N de 5» con el comentario y ya no ofrece otra (CA03). Si el caso ya estaba evaluado (por ejemplo, desde otro dispositivo), se muestra el rechazo y la evaluación registrada. Un reintento técnico usa el mismo identificador de solicitud. No evaluar no restringe ninguna función (CA06).
- Capturas: [caso cerrado](capturas/movil/76-hu32-evaluar.png), [sin puntuación](capturas/movil/77-hu32-sin-puntuacion.png), [formulario](capturas/movil/78-hu32-formulario.png) y [evaluado](capturas/movil/79-hu32-evaluado.png); en el panel, [historial con la evaluación](capturas/panel/hu33-evaluacion-historial.png) (HU33).

## Notificaciones del ciudadano (HU31, Sprint 3)

- `Servicios/Bandeja.cs` (`ServicioBandeja`), `Vistas/NotificacionesPagina.xaml` y `VistasModelo/NotificacionesVistaModelo.cs`: la bandeja de avisos por cambios de estado público de los casos propios. Las genera el servidor y persisten.
- El inicio muestra «Notificaciones (N sin leer)», actualizado cada vez que aparece. Tocar una notificación la marca como leída y abre el caso en «Mis casos».
- Sin push al ciudadano (E4): la bandeja se consulta al abrir la app y al volver al inicio.

## Acompañamiento temporal (HU34–HU37, Sprint 4)

- `Servicios/Acompanamiento.cs` (`ServicioAcompanamiento`, único en la app), `Vistas/AcompanamientoPagina.xaml` y `VistasModelo/AcompanamientoVistaModelo.cs`. Se abre con el botón «Acompañamiento» del inicio, separado del SOS y del reporte: no emite ninguno de los dos (HU34-CA05).
- **Inicio (HU34):** exige marcar «Acepto compartir mi ubicación con la central durante el trayecto» (CA02), el permiso de ubicación (si no está decidido, se explica y se pide; si se denegó, se orienta a los ajustes) y una posición actual leída tras la pulsación (CA03). Sin cualquiera de ellos no se llama al servidor. La sesión solo se muestra activa, y solo se transmite, con la respuesta del servidor (CA07). Sin respuesta, «Reintentar el inicio» reutiliza el mismo `IdSolicitud`.
- **Estado (CA01, CA08):** tiempo restante (con la hora del servidor, no la del teléfono), hora de fin y «Finalizar acompañamiento». El inicio muestra «Acompañamiento activo… Quedan N min.» y no deja salir de la sesión hasta finalizarlo. Al volver a abrir la app se consulta la sesión activa y se reanuda el envío.
- **Transmisión (HU35):** cada 15 s (lo indica el servidor) se lee la posición actual (como máximo 10 s); si no hay una nueva, se reenvía la última disponible con su hora de obtención original y se avisa «Sin posiciones nuevas» (CA01, CA03). Se muestra la condición real: sin conexión, sin permiso o sin posición (CA04). **DP13 (16/10/2026):** la transmisión sigue con la app en segundo plano, con la pantalla apagada o cerrada desde recientes, gracias a un servicio en primer plano de tipo ubicación (`Platforms/Android/SegundoPlano.cs`) con la notificación permanente «Acompañamiento activo» ([captura](capturas/movil/71-acompanamiento-notificacion.png)); se detiene al finalizar, al vencer o al salir de la sesión. Basta el permiso de ubicación «mientras se usa la app», porque el servicio lo inicia el ciudadano con la app abierta. Sin conexión no se acumulan posiciones (CA05: el aviso fijo lo dice).
- **Finalización (HU36):** el envío se detiene antes de llamar al servidor (CA01). Sin conexión, el cierre queda guardado (`Preferences`, solo el identificador de la sesión) y se reenvía cada 15 s y al volver a abrir la app (CA03). Mientras tanto el servidor la sigue viendo activa: es el efecto pendiente de DP20.
- **Vencimiento (HU37):** al cumplirse los 30 minutos la app deja de transmitir y consulta cómo terminó (CA02). Si el servidor ya la cerró, la respuesta 409 a una posición tiene el mismo efecto. Al reconectar se muestra «terminó a los 30 minutos» (CA06).
- **Verificación en el emulador (16/10/2026)** contra el servidor local: [antes de iniciar](capturas/movil/61-acompanamiento-antes-de-iniciar.png), [sin la finalidad aceptada](capturas/movil/62-acompanamiento-sin-finalidad.png), [activo](capturas/movil/63-acompanamiento-activo.png), [inicio con el acompañamiento activo](capturas/movil/64-inicio-acompanamiento-activo.png), [sin conexión](capturas/movil/65-acompanamiento-sin-conexion.png), [cierre pendiente](capturas/movil/66-acompanamiento-cierre-pendiente.png), [finalizado](capturas/movil/67-acompanamiento-finalizado.png) y [vencido](capturas/movil/68-acompanamiento-vencido.png).

## Confirmación de bienestar (HU41, Sprint 5)

- Durante un acompañamiento, la app consulta en cada envío de posición (cada 15 s) si la central pidió una confirmación. La pantalla «Acompañamiento» la muestra arriba como «¿Cómo estás?», con «Estoy bien» y «Necesito ayuda». La presentación se registra cuando la pantalla está a la vista; con la app en segundo plano no, aunque la página siga abierta: se escuchan la pausa y la reanudación de la ventana.
- Con la app en segundo plano, una notificación del canal «Confirmación de bienestar» (prioridad alta) avisa «La central pregunta cómo estás». Al tocarla se abre la app (`Platforms/Android/SegundoPlano.cs`).
- «Necesito ayuda» prepara el SOS como el botón SOS: activación, ubicación actual de hasta 10 s y descarte de una posición anterior (HU08, DP11). Se envía con la respuesta y la app solo lo confirma con la respuesta del servidor. Sin respuesta lo indica claramente y el reintento reenvía el mismo SOS (mismo identificador), así que el servidor no lo duplica.
- Capturas: [confirmación](capturas/movil/80-hu41-confirmacion.png), [«Estoy bien»](capturas/movil/81-hu41-estoy-bien.png), [notificación en segundo plano](capturas/movil/82-hu41-notificacion.png) y [«Necesito ayuda»](capturas/movil/83-hu41-necesito-ayuda.png); en el panel, [la solicitud](capturas/panel/hu40-solicitada.png) y [la respuesta con su SOS](capturas/panel/hu41-necesito-ayuda-central.png).

## Integración continua

`.github/workflows/ci-movil.yml` se ejecuta en `windows-latest` solo cuando cambian la app, los contratos, la solución o el propio flujo. Instala `maui-android` y las dependencias de Android, ejecuta `Cad.Movil.Tests` y compila el APK en Release. La CI del servidor (`ci.yml`) sigue en Linux y no compila la app.

## Pruebas automatizadas

`Cad.Movil.Tests`: 165 pruebas al 14/10/2026 (entre ellas las 40 del reporte de HU10, en `ReporteTests`); la descripción que sigue es la del Sprint 1. 54 pruebas (EmisorSos, PreparacionUbicacion, ApiCad con un `HttpMessageHandler` falso, ValidacionDireccion y modelos de vista). Pruebas de mutación sobre `EmisorSos`, todas detectadas: sin el filtro de la posición anterior, con un `IdSolicitud` nuevo en el reintento, sin la activación única, confirmando antes de la respuesta y reintentando los rechazos.
