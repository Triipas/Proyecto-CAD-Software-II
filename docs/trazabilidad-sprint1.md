# Trazabilidad del Sprint 1

Sprint 1 — Entorno, cuentas, permisos y emisión/recepción inicial de SOS.
IDs de tarea y resultados verificables tomados del Plan Scrum (apartado 3.2). Estados según el Plan Scrum 3.3: *Pendiente*, *En curso*, *Implementada*, *Integrada*, *Aceptada*.

> Desfase de calendario: el Plan Scrum fija el Sprint 1 del 07/09/2026 al 16/09/2026. El desarrollo comienza el 12/10/2026. La replanificación corresponde al Product Owner; este documento no modifica fechas ni alcance.

## Estado de las tareas

| ID | HU/PBI | Tarea | Estado | Evidencia |
|---|---|---|---|---|
| S1-T01 | TEC01 | Procedimiento de despliegue repetible | En curso | Bloque 1: solución local con `/salud` y pruebas. Falta el despliegue publicado (hosting pendiente). |
| S1-T02 | TEC01 | Datos de prueba por rol y canal cifrado | Pendiente | — |
| S1-T03 | SPIKE01 | Captura y transmisión de ubicación en 4 condiciones | Pendiente | — |
| S1-T04 | SPIKE01 | Nota técnica | Pendiente | — |
| S1-T05 | HU01 | Registro con validaciones y unicidad bajo concurrencia | Implementada y verificada en local (falta el entorno publicado) | `POST /api/cuentas/registro`; `RegistroCiudadanoTests` CA01–CA05 en verde contra PostgreSQL 17 |
| S1-T06 | HU01 | Hash de contraseña documentado | Implementada y verificada en local (falta revisar los registros del entorno publicado) | [seguridad-contrasenas.md](seguridad-contrasenas.md); `RegistroCiudadanoTests` CA06 en verde |
| S1-T07 | HU02 | Inicio de sesión por rol | Implementada y verificada en local | App: `POST /api/auth/token` (JWT). Panel: `/panel/ingresar` (cookie). `InicioSesionAppTests` e `InicioSesionPanelTests`: CA01–CA04 en verde. CA05 (canal cifrado) corresponde a S1-T08. |
| S1-T08 | HU02 | Credenciales solo por canal cifrado | Pendiente | — |
| S1-T09 | HU03 | Matriz de permisos y rechazo de solicitudes directas | Pendiente | — |
| S1-T10 | HU04 | Gestión de cuentas institucionales | Pendiente | — |
| S1-T11 | HU04 | Sin contraseñas en claro; solo administrador | Pendiente | — |
| S1-T12 | HU06 | Estados del permiso de geolocalización | Pendiente | — |
| S1-T13 | HU07 | SOS con una activación, confirmado tras el servidor | Pendiente | — |
| S1-T14 | HU07 | Idempotencia reutilizable | Pendiente | — |
| S1-T15 | HU08 | SOS con y sin ubicación actual | Pendiente | — |
| S1-T16 | HU07, HU08 | Aceptación conjunta | Pendiente | — |
| S1-T17 | HU11 | Tiempo real y sección urgente (+ mapa base mínimo) | Pendiente | — |
| S1-T18 | HU11 | Medición de RNF02 | Pendiente | — |
| S1-T19 | HU03 | Registro acumulativo de verificación por rol | Pendiente | — |
| S1-T20 | Transversal | Revisión de registros | Pendiente | — |
| S1-T21 | TEC01, HU01–HU11 | Integración y demostración de extremo a extremo | Pendiente | — |

## Decisiones del equipo (12/10/2026)

| Tema | Decisión |
|---|---|
| Plataforma servidor | .NET 10 (SDK 10.0.401 instalado), según el Plan de Arquitectura. |
| Repositorio | Repositorio Git local creado en la carpeta del proyecto; sin remoto por ahora. |
| App móvil | .NET MAUI (aplicación nativa Android), según ADR-01, en lugar de la PWA del Plan Scrum 1.4. |
| Base de datos local | PostgreSQL en Docker. |
| DP02 — alta de administradores | Administrador inicial sembrado mediante variables de entorno, solo para validación. |
| HU11-CA01 — mapa | Mapa base mínimo en el Sprint 1 (dependencia mínima de HU25, aprobada). |
| DP01 — datos del registro | **Validada.** Obligatorios: nombres, apellidos, correo electrónico (identificador de acceso, normalizado y único), teléfono móvil (9 dígitos, empieza por 9) y contraseña (8–128 caracteres, al menos una letra y un número). No se pide DNI. |
| DP04 — «operador autorizado» | Propuesta (pendiente de validación): cualquier cuenta activa con rol Operador, mediante políticas con nombre propio para poder restringirla después. El administrador no recibe permisos de operador. |
| DP11 — ubicación del SOS | Propuesta (pendiente de validación): solo cuenta como actual una posición obtenida después de la activación; espera máxima configurable (10 s); si se agota, «Ubicación no disponible»; precisión guardada sin umbral de rechazo. |
| DP14 — SOS repetidos | Propuesta (pendiente de validación): cada activación crea un SOS nuevo, aunque exista otro pendiente; solo los reintentos con el mismo IdSolicitud se tratan como el mismo SOS. |
| Hosting del entorno de validación | Pendiente. Cualquier opción con costo requiere autorización expresa. |

## Bloques ejecutados

| Bloque | Fecha | Tareas | Resultado |
|---|---|---|---|
| 1 — Esqueleto local | 12/10/2026 | S1-T01 (parcial) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 2/2 superadas. `GET /salud` → 200 `Healthy`. |
| 2 — Registro de ciudadano | 12/10/2026 | S1-T05, S1-T06 (HU01) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 22/22 superadas contra PostgreSQL 17 (Docker). Servidor real: 201 / 409 / 400 con motivos por campo. Sin contraseñas, correos ni teléfonos en los registros. |
| 3 — Inicio de sesión | 12/10/2026 | S1-T07 (HU02); DP02 | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 50/50 superadas. Servidor real: ciudadano obtiene JWT y consulta `/api/cuentas/yo`; contraseña incorrecta → 401; administrador sembrado entra a `/panel/admin`. Sin contraseñas, correos ni tokens en los registros. |

### Observaciones del bloque 2

- La prueba HTTP de CA05 (10 registros simultáneos) se resolvió en la comprobación previa del caso de uso: no llegó a producir una violación de unicidad en la base de datos. Por eso se añadió `CA05_IndiceUnico_*`, que verifica directamente que PostgreSQL rechaza la segunda cuenta con el mismo correo. La traducción de esa violación a 409 en `RegistrarCiudadano` está revisada en el código, pero no se ha forzado con una carrera real.
- HU01 no se acepta todavía: falta verificarla en el entorno publicado (RNF09 y S1-T21).

### Decisiones y observaciones del bloque 3

- **Canal por rol (hecho documentado, Plan de Arquitectura Tabla 3):** ciudadano y sereno usan la app (JWT); operador y administrador, el panel (cookie). El servidor rechaza el canal equivocado.
- **Mensajes:** correo inexistente y contraseña errónea reciben el mismo mensaje. «Cuenta desactivada» solo se muestra a quien acierta la contraseña.
- **DP02:** administrador inicial creado al arrancar desde `Admin:Email` y `Admin:ClaveInicial`. El cambio obligatorio de contraseña tras el primer acceso, mencionado como supuesto en la arquitectura, **no está implementado**.
- **DP03 respetada:** el token lleva el sello de seguridad, pero no se comprueba en cada solicitud. Revocar sesiones de cuentas desactivadas queda pendiente de la decisión.
- **Parámetros técnicos propuestos (configurables):** duración del JWT y de la sesión del panel, 480 min.
- **Pendiente:** limitación de intentos de inicio de sesión (opcional en la arquitectura, 9.6) y persistencia de las claves de Data Protection para el entorno publicado.
