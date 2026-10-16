# SPIKE01 — Captura y transmisión de ubicación en la app nativa

**Tareas:** S1-T03 (pruebas en cuatro condiciones) y S1-T04 (nota técnica). **No se presenta como funcionalidad entregada** (Plan Scrum).

**Estado: preliminar.** Las pruebas se hicieron en un emulador contra el servidor local. La repetición en un **teléfono Android real** contra `https://cad-servidor.onrender.com` y la decisión sobre esta nota están planificadas en el Sprint 3 (ver «Para cerrar SPIKE01»).

## Pregunta

¿En qué condiciones puede la app capturar la ubicación y transmitirla al servidor de forma fiable? De la respuesta dependen:
- qué condiciones se declaran validadas;
- qué puede afirmar la interfaz (HU07, HU08, HU18, HU35);
- si se reestiman HU18 y HU35.

## Método

Sonda de diagnóstico **solo en compilaciones Debug**, en la pantalla de ingreso: «Diagnóstico de ubicación (SPIKE01)». Código en `src/Cad.Movil/Servicios/Diagnostico/`. Cada 5 s:

1. Consulta el permiso de ubicación, sin solicitarlo nunca.
2. Pide una lectura **actual** con el mismo límite que el SOS: 10 s (DP11).
3. Comprueba la transmisión con `GET /salud`, con un límite de 5 s. **No envía coordenadas**: no hay todavía un caso de uso que lo justifique (RNF07).
4. Registra en un CSV el plano de la app, la red, el permiso, el resultado de la captura (precisión y antigüedad, **sin coordenadas**) y el de la transmisión.

Las cuatro condiciones se forzaron con `adb`:
- **Segundo plano:** botón de inicio de Android.
- **Sin conexión:** modo avión.
- **Permiso denegado:** `pm revoke`.

## Entorno

| Elemento | Valor |
|---|---|
| Dispositivo | Emulador Android 15 (API 35, `google_apis`, x86_64), perfil Pixel |
| Ubicación | Simulada con `adb emu geo fix` (Lima) |
| Servidor | Local (`http://10.0.2.2:5220`) |
| App | `Cad.Movil` Debug, permiso «mientras se usa la app», sin servicio en primer plano |
| Fecha | 13/10/2026, 20:50–20:57 (Lima) = 14/10/2026 01:50–01:57 UTC |
| Datos | [2026-10-13-emulador-android15.csv](2026-10-13-emulador-android15.csv) (33 muestras) |

## Resultados

| Condición | Muestras | Captura de ubicación | Transmisión | Evidencia |
|---|---|---|---|---|
| **Primer plano, con conexión** | 14 | **14/14** · ± 5 m · lectura actual (0 s) en 3,6–5,0 s | **14/14** · 7–513 ms | [20](20-sonda-primer-plano.png) |
| **Segundo plano** (60 s) | 5 | **2/5.** Solo la primera muestra (4 s tras salir) y una lectura **de hace 56 s**; las otras 3 agotaron los 10 s sin lectura | **2/5.** 3 fallos (`WebException`, tiempo agotado ×2) | CSV |
| **Sin conexión** (modo avión, 30 s) | 7 | **7/7:** el GPS no necesita red | **1/7.** Las 6 primeras fallan al instante («sin conexión»); la última llega mientras se restablecía la red. Después se recupera sola | [21](21-sonda-sin-conexion.png) |
| **Permiso denegado** | 7 | **0/7:** «SinPermiso», sin volver a solicitarlo | 7/7 | [22](22-sonda-sin-permiso.png) |

**SOS y salida inmediata de la app** (1 ensayo): se pulsó SOS y, 0,3 s después, el botón de inicio. El SOS se registró en el servidor unos 6 s después, **con la app ya en segundo plano y con ubicación**; al volver se mostraba «SOS registrado» ([23](23-sos-tras-segundo-plano.png)). La lectura se completó antes de que Android limitara la app. Con una lectura más lenta, el resultado podría ser distinto.

## Hallazgos que cambiaron el código

1. **La lectura de ubicación puede no terminar nunca en segundo plano.** En el primer ensayo, la sonda se detuvo al salir de la app: la plataforma no entregó lectura y no respetó el límite de 10 s. Esto también podía dejar un **SOS sin enviar**.
   - **Corrección:** `ServicioUbicacionMaui` y `EmisorSos` aplican su propio límite de 10 s. Si no hay lectura, el SOS sale sin ubicación (HU08-CA02).
   - **Prueba:** `EmisorSosTests.HU08_CA02_UbicacionQueNoResponde_*` (mutación detectada).
2. **Durante un cambio de red, Android lanza `WebException`.** El cliente HTTP solo esperaba `HttpRequestException`, así que un SOS emitido al perder la cobertura podía cerrar la app.
   - **Corrección:** `ApiCad` trata esos errores como «sin respuesta», y `EmisorSos` convierte cualquier fallo inesperado en «sin respuesta» con reintento del mismo `IdSolicitud`.
   - **Pruebas:** `ApiCadTests.CambioDeRedEnAndroid_*` y `EmisorSosTests.HU07_CA04_FalloInesperadoDeLaApi_*` (mutaciones detectadas).
3. **En segundo plano el sistema devolvió una posición de hace 56 s.** Confirma la necesidad de DP11: el SOS ya descarta posiciones anteriores a la activación (HU08-CA03).

## Decisión propuesta (para aprobación del equipo)

1. **Condición que se declara validada:** app **en primer plano y con conexión**. Coincide con el Plan de Arquitectura, que excluye el seguimiento en segundo plano y el servicio en primer plano de Android (RNF11).
2. **Qué puede afirmar la interfaz:**
   - «SOS registrado» solo con la respuesta del servidor (ya implementado, HU07-CA03).
   - «Con tu ubicación» solo si la lectura es actual (ya implementado, HU08-CA03).
   - Sin conexión, el SOS **no** se da por enviado: se ofrece reintentarlo (ya implementado, HU07-CA04/CA07).
   - Para el sereno (HU18) y el acompañamiento (HU35), la interfaz no debe prometer que la ubicación se comparte con la app cerrada o en segundo plano. Debe decir que solo se comparte **mientras la app está abierta**, y la central debe distinguir una posición «antigua» o «sin actualizar». Reglas concretas, umbrales y textos: pendientes del equipo; no se inventan aquí.
3. **Reestimación de HU18 y HU35:** **no hace falta reestimar al alza** si se mantiene el alcance «primer plano». Si el equipo quisiera seguimiento en segundo plano, haría falta un servicio en primer plano de Android y el permiso de ubicación en segundo plano. Eso contradice RNF11 y el Plan de Arquitectura, y exigiría reabrir la decisión.

## Para cerrar SPIKE01 (Sprint 3)

- **Repetir las cuatro condiciones en un teléfono Android real** contra el entorno validado: la app en Debug apuntando a `https://cad-servidor.onrender.com/` o al servidor local con `adb reverse tcp:5220 tcp:5220` y `http://localhost:5220/`. Anotar marca, modelo, versión de Android y la configuración de ahorro de batería: algunos fabricantes limitan las apps en segundo plano aún más que Android.
- Repetir el ensayo «SOS y salida inmediata» con varias lecturas lentas: interiores, con el GPS recién activado.
- Registrar la decisión del equipo sobre el punto 2 y la reestimación.
