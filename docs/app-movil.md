# App móvil CAD (.NET MAUI, Android)

App nativa del ciudadano y del sereno (ADR-01). En el Sprint 1 cubre: registro (HU01), ingreso por rol (HU02), preparación de la ubicación (HU06) y SOS con una única activación y ubicación actual (HU07, HU08). El modo sereno solo permite ingresar y salir; sus funciones llegan en el Sprint 2.

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

## Pruebas automatizadas

`Cad.Movil.Tests`: 54 pruebas (EmisorSos, PreparacionUbicacion, ApiCad con un `HttpMessageHandler` falso, ValidacionDireccion y modelos de vista). Pruebas de mutación sobre `EmisorSos`, todas detectadas: sin el filtro de la posición anterior, con un `IdSolicitud` nuevo en el reintento, sin la activación única, confirmando antes de la respuesta y reintentando los rechazos.
