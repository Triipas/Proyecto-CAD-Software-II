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

✔ = disponible, ✘ = rechazada (401, 403 o redirección al ingreso o a «acceso denegado»), sin devolver el contenido protegido. Verificado el 12/10/2026 contra PostgreSQL 17 (`MatrizAutorizacionTests`, 75 casos).

| Operación | HU | Anónimo | Ciudadano | Operador | Sereno | Administrador |
|---|---|---|---|---|---|---|
| `GET /salud` | TEC01 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `POST /api/cuentas/registro` | HU01 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `POST /api/auth/token` (la app; el caso de uso solo emite token a ciudadano y sereno) | HU02 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `GET /api/cuentas/yo` | HU02 | ✘ | ✔ | ✔ | ✔ | ✔ |
| `POST /api/sos` | HU07, HU08 | ✘ | ✔ | ✘ | ✘ | ✘ |
| `GET /panel/ingresar` | HU02 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `POST /panel/salir` | HU02 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `GET /panel/acceso-denegado` | HU03 | ✔ | ✔ | ✔ | ✔ | ✔ |
| `GET /panel/central` (sección urgente y su fragmento `?handler=Lista`) | HU11 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `POST /hubs/central/negotiate` y `/hubs/central` (tiempo real) | HU11 | ✘ | ✘ | ✔ | ✘ | ✘ |
| `GET /panel/admin` (página base de HU04) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |
| `GET /panel/admin/cuentas` (listado) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |
| `GET` y `POST /panel/admin/cuentas/nueva` (alta) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |
| `GET` y `POST /panel/admin/cuentas/{id}/editar` (modificación) | HU04 | ✘ | ✘ | ✘ | ✘ | ✔ |

**HU03-CA03 «no se modifica ningún dato»:** verificado con las primeras operaciones de escritura restringidas (HU04). Un ciudadano, un sereno, un operador con sesión y token antifalsificación válidos, o una solicitud sin sesión, intentan crear y modificar cuentas por solicitud directa: todas se rechazan, el número de cuentas no cambia y la cuenta atacada conserva sus datos y su contraseña (`CuentasInstitucionalesTests.CA06_*`). Prueba de mutación: sin la política `Administrador` en el alta, el operador llega a crear una cuenta y la prueba falla.

Las operaciones públicas lo son por definición (registro, inicio de sesión, salud). Que un rol pueda llamarlas no le da acceso a nada: el canal y la titularidad se validan dentro de cada caso de uso.

## B. Registro acumulativo HU04–HU43 (HU03-CA04)

Roles tomados del campo «Rol principal» y «Roles habilitados adicionales» de *Historias de usuario estructuradas*. Una HU pasa a «Verificada» solo cuando sus operaciones están en la matriz ejecutable y se prueban con los demás roles.

| HU | Operación | Rol(es) según la HU | Estado de la verificación por rol |
|---|---|---|---|
| HU04 | Crear, consultar y modificar cuentas institucionales | Administrador | **Verificada (12/10/2026):** listado, alta y modificación solo para Administrador; rechazo de solicitudes directas sin cambios en los datos. |
| HU05 | Desactivar una cuenta institucional | Administrador | Pendiente (Sprint 4) |
| HU06 | Preparar la geolocalización al abrir la aplicación | Ciudadano | Pendiente (Sprint 1; sin operación de servidor prevista) |
| HU07 | Emitir un SOS con una única activación | Ciudadano | **Verificada en el servidor (12/10/2026):** `POST /api/sos` solo para Ciudadano; solicitudes directas de los otros perfiles no crean casos (`SosTests.HU03_*`). La app (MAUI) está pendiente. |
| HU08 | Asociar la ubicación actual al SOS | Ciudadano | Verificada en el servidor junto con HU07 (misma operación). Captura en la app pendiente. |
| HU09 | Cancelar un SOS pendiente | Ciudadano | Pendiente (Sprint 5) |
| HU10 | Registrar un reporte de incidencia | Ciudadano | Pendiente (Sprint 3) |
| HU11 | Recibir los SOS en la sección de atención urgente | Operador | **Verificada (13/10/2026):** página, fragmento y canal en tiempo real solo para Operador; un ciudadano con JWT o una conexión sin sesión no pueden conectarse al hub (`RecepcionSosTests.HU03_*`). |
| HU12 | Contactar al ciudadano y registrar la verificación del SOS | Operador | Pendiente (Sprint 2) |
| HU13 | Clasificar un SOS después de su registro | Operador | Pendiente (Sprint 2; DP09) |
| HU14 | Iniciar la revisión de un reporte y corregir su categoría | Operador | Pendiente (Sprint 3) |
| HU15 | Admitir un reporte para atención | Operador | Pendiente (Sprint 3) |
| HU16 | Derivar un reporte | Operador | Pendiente (Sprint 3) |
| HU17 | Descartar un reporte con justificación | Operador | Pendiente (Sprint 3) |
| HU18 | Activar mi estado operativo y compartir mi ubicación | Sereno | Pendiente (Sprint 2) |
| HU19 | Monitorear el estado y la disponibilidad de los serenos | Operador | Pendiente (Sprint 2) |
| HU20 | Asignar un caso pendiente a un sereno disponible | Operador | Pendiente (Sprints 2 y 3) |
| HU21 | Recibir y consultar un despacho | Sereno | Pendiente (Sprint 2) |
| HU22 | Confirmar mi desplazamiento al caso | Sereno | Pendiente (Sprint 2) |
| HU23 | Registrar el inicio de atención | Sereno; Operador (queda identificado como responsable) | Pendiente (Sprint 2) |
| HU24 | Cerrar un caso con su tipificación y resultado | Sereno; Operador (queda identificado como responsable) | Pendiente (Sprint 2) |
| HU25 | Visualizar los casos en el mapa | Operador | Pendiente (Sprint 4; mapa base con HU11) |
| HU26 | Buscar y filtrar casos | Operador | Pendiente (Sprint 3) |
| HU27 | Consultar el historial de un caso | Operador | Pendiente (Sprint 3) |
| HU28 | Consultar los tiempos de respuesta y de atención | Operador | Pendiente (Sprint 5) |
| HU29 | Consultar el mapa de concentración de incidencias | Operador | Pendiente (Sprint 5) |
| HU30 | Consultar mis casos | Ciudadano | Pendiente (Sprint 3; titularidad) |
| HU31 | Recibir notificaciones de avances de mis casos | Ciudadano | Pendiente (Sprint 3; titularidad) |
| HU32 | Evaluar la atención de un caso cerrado | Ciudadano | Pendiente (Sprint 5; titularidad) |
| HU33 | Consultar las evaluaciones de atención | Operador (autorizado, DP04) | Pendiente (Sprint 5) |
| HU34 | Activar el acompañamiento temporal | Ciudadano | Pendiente (Sprint 4) |
| HU35 | Compartir mi posición durante el acompañamiento | Ciudadano | Pendiente (Sprint 4) |
| HU36 | Finalizar el acompañamiento cuando yo decida | Ciudadano | Pendiente (Sprint 4) |
| HU37 | Cierre automático del acompañamiento a los 30 minutos | Ciudadano (lo ejecuta el temporizador del sistema) | Pendiente (Sprint 4) |
| HU38 | Consultar los acompañamientos activos y sus posiciones | Operador (autorizado, DP04) | Pendiente (Sprint 4) |
| HU39 | Consultar el historial de sesiones de acompañamiento | Operador (autorizado, DP04) | Pendiente (Sprint 4; HU39-B bloqueada por DP22) |
| HU40 | Solicitar una confirmación de bienestar | Operador | Pendiente (Sprint 5) |
| HU41 | Responder una confirmación de bienestar | Ciudadano | Pendiente (Sprint 5) |
| HU42 | Recibir avisos técnicos y de verificación del acompañamiento | Operador | Pendiente (Sprint 5) |
| HU43 | Revisar avisos y registrar su atención | Operador | Pendiente (Sprint 5; DP19) |

## Al implementar una HU

1. Proteger cada endpoint o página con su política (`Politicas.*`) y, si corresponde, comprobar la titularidad en el caso de uso.
2. Añadir sus rutas a `MatrizAutorizacionTests.Matriz`. Si se olvida, falla `CA04_TodasLasRutasDelServidor_EstanDeclaradasEnLaMatriz`.
3. Actualizar la tabla B de este documento con el resultado y la fecha.
