# Matriz de permisos y registro acumulativo de verificación (HU03)

Tareas: **S1-T09** (matriz de permisos de los cuatro roles y rechazo de solicitudes directas) y **S1-T19** (registro acumulativo de verificación por rol). HU03 **no se acredita como terminada**: su CA04 exige verificar las operaciones de HU04 a HU43 a medida que se implementen (Plan Scrum, 3.2).

## Cómo se aplica

| Mecanismo | Dónde | Efecto |
|---|---|---|
| Políticas con nombre propio | `src/Cad.Server/Compartido/Autorizacion.cs` | `Ciudadano`, `Sereno`, `OperadorAutorizado`, `Administrador`. Cada endpoint o página declara la suya. |
| Denegación por defecto | Misma clase (`SetFallbackPolicy`) | Una ruta sin declaración exige sesión. Las rutas públicas se marcan expresamente (`AllowAnonymous`). Una ruta inexistente sin sesión responde 401 (`/api`) o lleva al ingreso (panel): no se revela qué existe. |
| Canal por rol | `IniciarSesion` (HU02) | Ciudadano y sereno: app (JWT). Operador y administrador: panel (cookie). Plan de Arquitectura, Tabla 3. |
| Autorización en el servidor | Políticas en cada endpoint y página | La interfaz no decide permisos: una solicitud directa se evalúa igual que una hecha desde la interfaz (HU03-CA03). |
| Matriz ejecutable | `tests/Cad.Server.Tests/Autorizacion/MatrizAutorizacionTests.cs` | Prueba cada operación sin sesión y con los cuatro roles, y **falla si el servidor tiene una ruta no declarada en la matriz**. |

### DP04 — «operador autorizado»

Decisión delegada al equipo de desarrollo y registrada el 12/10/2026: **cualquier cuenta activa con rol Operador**. Se implementa como la política `OperadorAutorizado` para poder restringirla más adelante sin modificar los endpoints (HU33, HU38 y HU39 la mencionan). El administrador **no** recibe permisos de operador.

## A. Operaciones existentes verificadas

✔ = disponible, ✘ = rechazada (401, 403 o redirección al ingreso o a «acceso denegado»), sin devolver el contenido protegido. Verificado el 16/10/2026 contra PostgreSQL 17 (`MatrizAutorizacionTests`, 217 casos: 43 operaciones con los cinco perfiles, más la comprobación de que todas las rutas están declaradas y la de denegación por defecto).

| Operación | HU | Anónimo | Ciudadano | Operador | Sereno | Administrador |
|---|---|---|---|---|---|---|
| `GET /salud` | TEC01 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `POST /api/cuentas/registro` | HU01 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `POST /api/auth/token` (la app; el caso de uso solo emite token a ciudadano y sereno) | HU02 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `GET /api/cuentas/yo` | HU02 | ✘ | ✔ | ✔ | ✔ | ✔ |
| `POST /api/sos` | HU07, HU08 | ✘ | ✔ | ✘ | ✘ | ✘ |
| `POST /api/reportes` y `GET /api/reportes/categorias` | HU10 | ✘ | ✔ | ✘ | ✘ | ✘ |
| `GET /api/casos/mios` y `GET /api/casos/mios/{id}` (solo los casos propios) | HU30 | ✘ | ✔ | ✘ | ✘ | ✘ |
| `GET /api/notificaciones` y `POST /api/notificaciones/{id}/leida` (solo las propias) | HU31 | ✘ | ✔ | ✘ | ✘ | ✘ |
| `POST /api/acompanamiento`, `GET /api/acompanamiento/actual` y `/{id}`, `POST /api/acompanamiento/{id}/posiciones` y `/finalizacion` (solo las sesiones propias) | HU34–HU37 | ✘ | ✔ | ✘ | ✘ | ✘ |
| `GET` y `PUT /api/sereno/estado`, `POST /api/sereno/posiciones` (estado operativo y posiciones del sereno) | HU18 | ✘ | ✘ | ✘ | ✔ | ✘ |
| `GET /api/mediciones/presentacion` (resultados de RNF02; 404 si la medición está apagada) | RNF02 | ✘ | ✘ | ✘ | ✘ | ✔ |
| `GET /panel/ingresar` | HU02 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `POST /panel/salir` | HU02 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `GET /panel/acceso-denegado` | HU03 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `GET /panel/central` (sección urgente y su fragmento `?handler=Lista`) | HU11 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `POST /hubs/central/negotiate` y `/hubs/central` (tiempo real) | HU11 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `GET /api/sereno/despacho`, `GET /api/sereno/casos/{id}` (solo el caso asignado al propio sereno), `PUT` y `DELETE /api/sereno/dispositivo` | HU21 | ✘ | ✘ | ✘ | ✔ | ✘ |
| `POST /api/sereno/casos/{id}/desplazamiento` y `/atencion` (solo el sereno asignado) | HU22, HU23 | ✘ | ✘ | ✘ | ✔ | ✘ |
| `POST /api/sereno/casos/{id}/cierre` (solo el sereno asignado) y `GET /api/sereno/categorias` | HU24 | ✘ | ✘ | ✘ | ✔ | ✘ |
| `POST /panel/central/sos/{id}?handler=Cerrar` (cierre por el operador) | HU24 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `POST /panel/central/sos/{id}?handler=IniciarAtencion` (inicio de atención por el operador) | HU23 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `POST /hubs/sereno/negotiate` y `/hubs/sereno` (aviso del despacho; cada sereno solo recibe los suyos) | HU21 | ✘ | ✘ | ✘ | ✔ | ✘ |
| `GET` y `POST /panel/central/sos/{id}` (verificación del SOS: teléfono, intentos de contacto, información y referencia; manejadores `Asignar` de HU20 y `Clasificar` de HU13) | HU12, HU13, HU20 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `GET /panel/reportes`, `GET` y `POST /panel/reportes/{id}` (reportes recibidos y en revisión; revisión, corrección de categoría, decisión y asignación de un admitido) | HU14–HU17, HU20 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `GET /panel/casos` (búsqueda de casos con filtros) | HU26 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `GET /panel/casos/{id}/historial` (historial del caso, con notas internas rotuladas) | HU27 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `GET /panel/admin` (página base de HU04) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |
| `GET /panel/admin/cuentas` (listado) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |
| `GET` y `POST /panel/admin/cuentas/nueva` (alta) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |
| `GET` y `POST /panel/admin/cuentas/{id}/editar` (modificación) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |

**HU03-CA03 «no se modifica ningún dato»:** verificado con las primeras operaciones de escritura restringidas (HU04). Un ciudadano, un sereno, un operador con sesión y token antifalsificación válidos, o una solicitud sin sesión, intentan crear y modificar cuentas por solicitud directa: todas se rechazan, el número de cuentas no cambia y la cuenta atacada conserva sus datos y su contraseña (`CuentasInstitucionalesTests.CA06_*`). Prueba de mutación: sin la política `Administrador` en el alta, el operador llega a crear una cuenta y la prueba falla.

Las operaciones públicas lo son por definición (registro, inicio de sesión, salud). Que un rol pueda llamarlas no le da acceso a nada: el canal y la titularidad se validan dentro de cada caso de uso.

## B. Registro acumulativo HU04–HU43 (HU03-CA04)

Las historias de los Sprints 1 y 2 de esta tabla fueron aceptadas en sus Sprint Review.

Roles tomados del campo «Rol principal» y «Roles habilitados adicionales» de *Historias de usuario estructuradas*. Una HU pasa a «Verificada» solo cuando sus operaciones están en la matriz ejecutable y se prueban con los demás roles.

| HU | Operación | Rol(es) según la HU | Estado de la verificación por rol |
|---|---|---|---|
| HU04 | Crear, consultar y modificar cuentas institucionales | Administrador | **Verificada (12/10/2026):** listado, alta y modificación solo para Administrador; rechazo de solicitudes directas sin cambios en los datos. |
| HU05 | Desactivar una cuenta institucional | Administrador | **Verificada (16/10/2026):** la acción `Desactivar` de `/panel/admin/cuentas/{id}/editar` solo para Administrador; un operador con sesión y token antifalsificación válido es enviado a «acceso denegado», y un anónimo o la app de un ciudadano o un sereno son rechazados, sin cambiar la cuenta (`DesactivacionCuentaTests.CA05_*`). Solo cuentas de operador o sereno: un ciudadano o un administrador responden 404. DP03 (16/10/2026): al desactivar, la sesión abierta deja de valer en el servidor en cualquier ruta (`DesactivacionCuentaTests.CA04_*`). |
| HU06 | Preparar la geolocalización al abrir la aplicación | Ciudadano | **Verificada (13/10/2026), sin operación de servidor:** solo existe en la pantalla del modo ciudadano. La app lleva a cada rol a su modo según el rol del token (`IngresoVistaModeloTests.HU02_CadaRolEntraEnSuModo_*`) y el modo sereno no la incluye. `PreparacionUbicacion` no recibe la API, así que no puede enviar coordenadas con ningún rol (`PreparacionUbicacionTests.CA04_*`). |
| HU07 | Emitir un SOS con una única activación | Ciudadano | **Verificada (13/10/2026).** Servidor: `POST /api/sos` solo para Ciudadano; las solicitudes directas de los otros perfiles no crean casos (`SosTests.HU03_*`). App: el SOS solo está en el modo ciudadano; un rechazo 401 o 403 del servidor no se reintenta ni se presenta como registrado (`EmisorSosTests.RechazoDelServidor_*`); verificado en emulador contra el servidor ([app-movil.md](app-movil.md)). |
| HU08 | Asociar la ubicación actual al SOS | Ciudadano | **Verificada (13/10/2026)** junto con HU07 (misma operación). En la app, la ubicación solo se lee con el permiso del ciudadano ya concedido (`EmisorSosTests.HU08_*`); verificado en emulador con y sin ubicación. |
| HU09 | Cancelar un SOS pendiente | Ciudadano | Pendiente (Sprint 5) |
| HU10 | Registrar un reporte de incidencia | Ciudadano | **Verificada en el servidor (14/10/2026):** `POST /api/reportes` y `GET /api/reportes/categorias` solo para Ciudadano; las solicitudes directas de un anónimo, un operador, un sereno o un administrador no crean casos (`ReporteTests.HU03_*`, matriz). App (14/10/2026): el formulario solo está en el modo ciudadano y una sesión terminada no envía el reporte (`ReporteTests.SesionVencida_*`); verificado en el emulador. |
| HU11 | Recibir los SOS en la sección de atención urgente | Operador | **Verificada (13/10/2026):** página, fragmento y canal en tiempo real solo para Operador; un ciudadano con JWT o una conexión sin sesión no pueden conectarse al hub (`RecepcionSosTests.HU03_*`). |
| HU12 | Contactar al ciudadano y registrar la verificación del SOS | Operador | **Verificada (13/10/2026):** consulta y registro solo para Operador; un administrador con token antifalsificación válido, o un ciudadano o un sereno con su token, no ven el teléfono ni registran intentos (`VerificacionSosTests.CA08_*`). Prueba de mutación: sin la política de operador, 9 pruebas fallan. |
| HU13 | Clasificar un SOS después de su registro | Operador | **Verificada en el servidor (14/10/2026):** clasificar (`POST /panel/central/sos/{id}?handler=Clasificar`) solo para Operador (DP09); un administrador con token antifalsificación válido es redirigido a «acceso denegado» y la app de un sereno o un ciudadano no clasifica, sin modificar el caso (`ClasificacionTests.DP09_*`). |
| HU14 | Iniciar la revisión de un reporte y corregir su categoría | Operador | **Verificada (15/10/2026):** `/panel/reportes` y `/panel/reportes/{id}` (incluidas sus acciones) solo para Operador; un administrador es enviado a «acceso denegado» también al enviar la acción directamente, y la app de un ciudadano o un sereno no obtiene las páginas (`RevisionReporteTests.HU03_*`, matriz). |
| HU15 | Admitir un reporte para atención | Operador | **Verificada (15/10/2026):** la acción «Decidir» de `/panel/reportes/{id}` solo para Operador; un administrador es enviado a «acceso denegado» también con una solicitud directa (`DecisionReporteTests.HU03_*`), y la app de un ciudadano o un sereno no obtiene la página. |
| HU16 | Derivar un reporte | Operador | **Verificada (15/10/2026):** la acción «Decidir» de `/panel/reportes/{id}` solo para Operador; un administrador es enviado a «acceso denegado» también con una solicitud directa (`DecisionReporteTests.HU03_*`), y la app de un ciudadano o un sereno no obtiene la página. |
| HU17 | Descartar un reporte con justificación | Operador | **Verificada (15/10/2026):** la acción «Decidir» de `/panel/reportes/{id}` solo para Operador; un administrador es enviado a «acceso denegado» también con una solicitud directa (`DecisionReporteTests.HU03_*`), y la app de un ciudadano o un sereno no obtiene la página. |
| HU18 | Activar mi estado operativo y compartir mi ubicación | Sereno | **Servidor verificado (13/10/2026):** estado y posiciones solo para Sereno; un ciudadano con su token no crea estado ni posiciones (`EstadoOperativoTests.CA05_*`). Mutación: sin la política de sereno, 10 pruebas fallan. App verificada (14/10/2026): sin permiso de ubicación no se activa ni se llama al servidor; la pantalla del sereno solo existe en el modo sereno. |
| HU19 | Monitorear el estado y la disponibilidad de los serenos | Operador | **Verificada (14/10/2026):** sección de serenos y fragmento `?handler=Serenos` solo para Operador (página `/panel/central`); un administrador es redirigido a «acceso denegado» y la app de un sereno o un ciudadano no los obtiene (`MonitoreoSerenosTests.CA05_*`). El aviso `SerenoActualizado` va solo al grupo de operadores del canal de la central. |
| HU20 | Asignar un caso pendiente a un sereno disponible | Operador | **Verificada en el servidor (14/10/2026):** asignar (`POST /panel/central/sos/{id}?handler=Asignar`) solo para Operador; un administrador con token antifalsificación válido es redirigido a «acceso denegado» y la app de un sereno o un ciudadano no asigna, sin crear registros (`AsignacionTests.CA09_*`). Mutación: sin la política de operador, 3 pruebas fallan. CA07 (15/10/2026): un reporte admitido se asigna desde la central con la acción `Asignar` de `/panel/reportes/{id}`, solo para Operador y con las mismas reglas (`DecisionReporteTests.HU15_CA02_HU20_CA07_*`). |
| HU21 | Recibir y consultar un despacho | Sereno | **Verificada en el servidor (14/10/2026):** solo el sereno con la asignación vigente consulta el caso; otro sereno recibe 404 y un ciudadano o un operador con su sesión son rechazados (`DespachoTests.CA04_*`). El aviso en tiempo real solo llega al sereno asignado (`CA01_*`). Datos limitados por DP06 (`CA05_*`). |
| HU22 | Confirmar mi desplazamiento al caso | Sereno | **Verificada en el servidor (14/10/2026):** solo el sereno asignado confirma; otro sereno recibe 404 y un operador es rechazado por la política y por el caso de uso (`AtencionTests.HU22_CA03_*`, `HU22_CA04_*`). |
| HU23 | Registrar el inicio de atención | Sereno; Operador (queda identificado como responsable) | **Verificada en el servidor (14/10/2026):** el sereno asignado (API) o un operador (panel); otro sereno recibe 404 y un administrador con token antifalsificación válido es redirigido a «acceso denegado» (`AtencionTests.HU23_*`). Reportes en curso (16/10/2026): la acción `IniciarAtencion` de `/panel/reportes/{id}`, solo para Operador (`AtencionReporteTests`). |
| HU24 | Cerrar un caso con su tipificación y resultado | Sereno; Operador (queda identificado como responsable) | **Verificada en el servidor (14/10/2026):** cierra el sereno asignado (API) o un operador (panel); otro sereno recibe 404 y un administrador con token antifalsificación válido es redirigido a «acceso denegado» (`CierreTests`). Las notas internas no llegan a ninguna ruta del ciudadano (`CierreTests.CA04_*`, DP05). Reportes en curso (16/10/2026): la acción `Cerrar` de `/panel/reportes/{id}`, solo para Operador (`AtencionReporteTests`). |
| HU25 | Visualizar los casos en el mapa | Operador | Pendiente (Sprint 4; mapa base con HU11) |
| HU26 | Buscar y filtrar casos | Operador | **Verificada (15/10/2026):** `/panel/casos` solo para Operador; un administrador es enviado a «acceso denegado» y la app de un ciudadano o un sereno no obtiene la página (`BusquedaCasosTests.CA06_*`, matriz). |
| HU27 | Consultar el historial de un caso | Operador | **Verificada (14/10/2026 para los SOS; 16/10/2026 para los reportes, S3-T16):** `/panel/casos/{id}/historial` solo para Operador, con el mismo control para los SOS y los reportes (revisión, corrección, decisión, derivación y descarte incluidos); un administrador es redirigido a «acceso denegado» y la app de un ciudadano o un sereno no lo obtiene ni ve las notas internas (`HistorialTests.CA04_CA06_*`, `CA06_*`, matriz). La cláusula «Cancelada» (CA02) llega con HU09. |
| HU28 | Consultar los tiempos de respuesta y de atención | Operador | Pendiente (Sprint 5) |
| HU29 | Consultar el mapa de concentración de incidencias | Operador | Pendiente (Sprint 5) |
| HU30 | Consultar mis casos | Ciudadano | **Verificada (16/10/2026):** `GET /api/casos/mios` y `/api/casos/mios/{id}` solo para Ciudadano y solo sus casos; el caso de otro ciudadano responde 404 aunque se conozca su identificador; un anónimo, un sereno o un operador reciben 401/403 (`MisCasosTests.CA04_*`, matriz). Sin notas internas, intentos de contacto, operador ni sereno (`MisCasosTests.CA05_*`). |
| HU31 | Recibir notificaciones de avances de mis casos | Ciudadano | **Verificada (16/10/2026):** `GET /api/notificaciones` y `POST /api/notificaciones/{id}/leida` solo para Ciudadano y solo sus notificaciones; otro ciudadano no las ve ni puede marcarlas (404); un anónimo, un sereno o un operador reciben 401/403 (`NotificacionesCiudadanoTests.CA04_*`, matriz). |
| HU32 | Evaluar la atención de un caso cerrado | Ciudadano | Pendiente (Sprint 5; titularidad) |
| HU33 | Consultar las evaluaciones de atención | Operador (autorizado, DP04) | Pendiente (Sprint 5) |
| HU34 | Activar el acompañamiento temporal | Ciudadano | **Verificada en el servidor (16/10/2026):** `POST /api/acompanamiento` y `GET /api/acompanamiento/actual` solo para Ciudadano; un operador o un sereno reciben 403 sin crear sesiones (`AcompanamientoTests.HU34_OtrosPerfiles_*`, matriz). Otro ciudadano no ve ni usa la sesión (404). |
| HU35 | Compartir mi posición durante el acompañamiento | Ciudadano | **Verificada en el servidor (16/10/2026):** `POST /api/acompanamiento/{id}/posiciones` solo para el ciudadano titular; otros perfiles 403, otro ciudadano 404, sin registrar posiciones. |
| HU36 | Finalizar el acompañamiento cuando yo decida | Ciudadano | **Verificada en el servidor (16/10/2026):** `POST /api/acompanamiento/{id}/finalizacion` solo para el ciudadano titular; otros perfiles 403, otro ciudadano 404, sin cerrar la sesión. |
| HU37 | Cierre automático del acompañamiento a los 30 minutos | Ciudadano (lo ejecuta el temporizador del sistema) | **Verificada en el servidor (16/10/2026):** el cierre lo hace el temporizador, sin ninguna ruta; `GET /api/acompanamiento/{id}` solo para el ciudadano titular (otro ciudadano 404). |
| HU38 | Consultar los acompañamientos activos y sus posiciones | Operador (autorizado, DP04) | Pendiente (Sprint 4) |
| HU39 | Consultar el historial de sesiones de acompañamiento | Operador (autorizado, DP04) | Pendiente (Sprint 4; HU39-B bloqueada por DP22) |
| HU40 | Solicitar una confirmación de bienestar | Operador | Pendiente (Sprint 5) |
| HU41 | Responder una confirmación de bienestar | Ciudadano | Pendiente (Sprint 5) |
| HU42 | Recibir avisos técnicos y de verificación del acompañamiento | Operador | Pendiente (Sprint 5) |
| HU43 | Revisar avisos y registrar su atención | Operador | Pendiente (Sprint 5; DP19) |

**Cierre del Sprint 3 (S3-T16, 16/10/2026):** las operaciones de HU10, HU14–HU17, HU20-CA07, HU26, HU27, HU30 y HU31 están en la matriz ejecutable y se rechazan con los demás perfiles, también ante solicitudes directas. HU03 sigue sin acreditarse: faltan las operaciones de los Sprints 4 y 5.

## Al implementar una HU

1. Proteger cada endpoint o página con su política (`Politicas.*`) y, si corresponde, comprobar la titularidad en el caso de uso.
2. Añadir sus rutas a `MatrizAutorizacionTests.Matriz`. Si se olvida, falla `CA04_TodasLasRutasDelServidor_EstanDeclaradasEnLaMatriz`.
3. Actualizar la tabla B de este documento con el resultado y la fecha.
