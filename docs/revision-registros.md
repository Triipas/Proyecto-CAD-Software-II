# S1-T20 — Revisión de los registros del Sprint 1

**Tarea (Plan Scrum):** revisar los registros del sprint para comprobar que no contienen contraseñas ni datos enviados fuera del canal cifrado. Relacionada con RNF01 (credenciales), RNF07 (ubicación) y el Plan de Arquitectura 9.7 (información que no debe aparecer en registros).

**Fecha:** 13/10/2026 · **Resultado:** sin datos sensibles en los registros revisados.

## Qué se registra y con qué nivel

| Entorno | Configuración | Efecto |
|---|---|---|
| Todos (`appsettings.json`) | `Default: Information`, `Microsoft.AspNetCore: Warning` | No se registran las líneas de cada petición ni sus cadenas de consulta (incluido el identificador de conexión de SignalR). |
| Producción (`appsettings.Production.json`) | Formato JSON; `Microsoft.EntityFrameworkCore.Database.Command: Warning` | No se registra el SQL de cada consulta. |
| Siempre | EF Core sin `EnableSensitiveDataLogging`; Npgsql sin registro de parámetros | Los parámetros SQL aparecen como `?`. |
| Código propio | Solo identificadores internos (UUID de caso o de usuario) y eventos | p. ej. «SOS {CasoId} registrado»; una ubicación descartada se anota sin coordenadas. |

Nunca se registran cuerpos de petición o respuesta, cabeceras, cookies ni tokens.

## Revisión automatizada (`RevisionRegistrosTests`)

Recorre en una sola ejecución el flujo completo del sprint, con datos ficticios únicos:

1. Registro de una ciudadana (HU01).
2. Inicio de sesión en la app, fallido y correcto, y consulta con el JWT (HU02).
3. Ingreso del administrador al panel, alta de un operador y cambio de su contraseña (HU02, HU04).
4. Ingreso del operador al panel con la contraseña antigua (rechazado) y con la nueva (HU02).
5. SOS con ubicación precisa recibido en tiempo real por un operador conectado al hub, y lista de la central (HU07, HU08, HU11).
6. Sprint 2 (S2-T16): el operador ve el teléfono y registra un intento con información del hecho y referencia (HU12); un sereno se activa y envía posiciones (HU18); registra el token de su dispositivo y recibe el despacho por push (HU21, con un doble de FCM); el operador clasifica (HU13); el sereno va en camino, inicia la atención y cierra con un resultado y notas internas (HU22–HU24).
7. Sprint 3 (S3-T17, prueba `S3T17_FlujoDeReportesYSeguimiento_*`): una ciudadana registra tres reportes, uno por cada fuente de la ubicación del hecho (punto del mapa, posición actual elegida y referencia), con descripciones y referencias que contienen datos personales; el operador abre la bandeja y cada detalle, inicia la revisión, corrige la categoría, admite uno, deriva otro (destino, acción y motivo) y descarta el tercero (motivo) (HU10, HU14–HU17); el admitido se asigna desde la central a un sereno, que lo atiende y lo cierra con resultado y notas internas (HU20-CA07, HU21–HU24); el operador busca con filtros y abre los historiales (HU26, HU27); la ciudadana consulta «Mis casos», un detalle y su bandeja, y marca una notificación como leída (HU30, HU31).

Después busca cada valor sensible en **todas** las entradas capturadas, en **todos los niveles** (incluidos Debug y Trace). Es más estricto que la configuración real, que no registra esos niveles para el framework.

| Categoría | Valores buscados | Hallazgos |
|---|---|---|
| Contraseñas | De la ciudadana, del operador (inicial y nueva), del administrador y las de los intentos fallidos | 0 |
| Tokens y cookies | JWT emitido, cookies `cad.panel` del administrador y del operador, sellos de seguridad de Identity, token del dispositivo del sereno (ADR-07) y cualquier texto con forma de JWT | 0 |
| Datos personales | Correos, teléfono y apellidos ficticios; información del hecho y referencia (HU12); resultado del cierre y notas internas (HU24) | 0 |
| Coordenadas | Latitud y longitud del SOS y de las posiciones del sereno, con punto y con coma decimal | 0 |
| Secretos del servidor | Clave de firma JWT, contraseña de la base de datos y cualquier `Password=` | 0 |

**La prueba detecta fugas reales:** al añadir temporalmente un registro de nivel Debug con las coordenadas del SOS, falla y las señala («coordenadas: -12.07…»). Después se restauró el código.

### Sprint 3 (S3-T17, 16/10/2026)

**Resultado:** sin datos sensibles. Se buscaron, además de las categorías anteriores, las descripciones y referencias de los reportes, las coordenadas del lugar del hecho (punto del mapa y posición actual), los motivos de las tres decisiones, el destino y la acción de la derivación, el resultado y las notas internas del cierre, la contraseña y el token de la ciudadana y la cookie del operador: 0 hallazgos. Los registros del Sprint 3 solo llevan el identificador del caso, la fuente de la ubicación del hecho y el tipo de decisión.

**La prueba detecta fugas reales:** tres mutaciones temporales —registrar la descripción del reporte (Debug), el destino de una derivación (Information) y la latitud del lugar del hecho (Trace)— hacen fallar la prueba; después se restauró el código.

**Posiciones no elegidas por el ciudadano (S3-T17):** un reporte no guarda la ubicación del dispositivo (las columnas del caso quedan vacías y una referencia con coordenadas se rechaza, también en la base de datos: `ReporteTests`); la posición actual solo se lee cuando la ciudadana elige «Usar mi posición actual» (`Cad.Movil.Tests/ReporteTests`, verificado en el emulador en el bloque 3). El despacho de un reporte lleva el lugar del hecho, nunca la ubicación del dispositivo (bloque 5 y demostración).

### Sprint 4 (S4-T18, 16/10/2026)

**Resultado:** sin datos sensibles. Prueba `S4T18_AcompanamientoSupervisionYDesactivacion_*`: una ciudadana con teléfono inicia un acompañamiento y envía posiciones precisas (también una inválida y un reenvío), lo finaliza; el operador consulta los activos, el recorrido y el historial y conserva el recorrido con una finalidad; una sesión de hace 40 días pierde sus posiciones por plazo; el operador abre el mapa de casos; el administrador desactiva a un sereno con un caso asignado. Se buscaron las coordenadas de todas las posiciones (también la inválida), la finalidad, el teléfono, los correos, las contraseñas, los tokens, las cookies y los sellos de seguridad: 0 hallazgos. Los registros del sprint solo llevan identificadores de sesión, caso y cuenta, el tipo de evento y cuántas posiciones se eliminaron.

**La prueba detecta fugas reales:** registrar la latitud de una posición del acompañamiento (Debug) o la finalidad de una conservación (Information) la hace fallar.

**Sin posiciones fuera del acompañamiento activo:** el servidor no incorpora posiciones de una sesión cerrada o vencida, ni antes de que exista (`AcompanamientoTests.HU35_CA02_*`, `HU36_CA04_*`, `HU37_CA04_*`), y la app solo lee la posición después de que la ciudadana pulse «Iniciar» con la finalidad aceptada (`Cad.Movil.Tests/AcompanamientoTests.HU34_CA04_*`). En el emulador, los registros del teléfono y del servidor no contenían coordenadas.

## Otras comprobaciones del sprint

- **Contenedor de producción** (bloque 9): registros 100 % JSON, sin contraseña de la base de datos, contraseña del administrador, cadenas de conexión ni correos.
- **Por historia:** HU01-CA06, HU02-CA04, HU04-CA07 (contraseñas) y la prueba de RNF07 de HU07/HU08 (coordenadas), en sus respectivas pruebas.

## Fuera del alcance de esta revisión

- **«Datos enviados fuera del canal cifrado»:** en local el servidor usa HTTP. En el entorno publicado, HTTP se redirige a HTTPS (salvo `/salud`), con HSTS y cookie `Secure` (bloque 9). La comprobación final de RNF09 se hará en Render ([despliegue.md](despliegue.md), sección 3).
- **Registros del proveedor** (Render y Neon): se revisarán en el entorno publicado.
