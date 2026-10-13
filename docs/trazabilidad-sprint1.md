# Trazabilidad del Sprint 1

Sprint 1 — Entorno, cuentas, permisos y emisión/recepción inicial de SOS.
IDs de tarea y resultados verificables tomados del Plan Scrum (apartado 3.2). Estados según el Plan Scrum 3.3: *Pendiente*, *En curso*, *Implementada*, *Integrada*, *Aceptada*.

> Desfase de calendario: el Plan Scrum fija el Sprint 1 del 07/09/2026 al 16/09/2026. El desarrollo comienza el 12/10/2026. La replanificación corresponde al Product Owner; este documento no modifica fechas ni alcance.

## Estado de las tareas

| ID | HU/PBI | Tarea | Estado | Evidencia |
|---|---|---|---|---|
| S1-T01 | TEC01 | Procedimiento de despliegue repetible | Preparado y verificado con el contenedor en local; **despliegue en Render pendiente del equipo** | `Dockerfile` (sin root), `render.yaml` (plan gratuito, ADR-06), [despliegue.md](despliegue.md). CI en GitHub Actions. Contenedor ejecutado en Docker contra PostgreSQL. |
| S1-T02 | TEC01 | Datos de prueba por rol y canal cifrado | Preparado; pendiente de ejecutar en el entorno publicado | HTTPS forzado tras el proxy, HSTS y cookie Secure (`DespliegueTests`); procedimiento de datos por rol en [despliegue.md](despliegue.md), sección 3. |
| S1-T03 | SPIKE01 | Captura y transmisión de ubicación en 4 condiciones | Pendiente | — |
| S1-T04 | SPIKE01 | Nota técnica | Pendiente | — |
| S1-T05 | HU01 | Registro con validaciones y unicidad bajo concurrencia | Implementada y verificada en local (falta el entorno publicado) | `POST /api/cuentas/registro`; `RegistroCiudadanoTests` CA01–CA05 en verde contra PostgreSQL 17 |
| S1-T06 | HU01 | Hash de contraseña documentado | Implementada y verificada en local (falta revisar los registros del entorno publicado) | [seguridad-contrasenas.md](seguridad-contrasenas.md); `RegistroCiudadanoTests` CA06 en verde |
| S1-T07 | HU02 | Inicio de sesión por rol | Implementada y verificada en local | App: `POST /api/auth/token` (JWT). Panel: `/panel/ingresar` (cookie). `InicioSesionAppTests` e `InicioSesionPanelTests`: CA01–CA04 en verde. CA05 (canal cifrado) corresponde a S1-T08. |
| S1-T08 | HU02 | Credenciales solo por canal cifrado | Preparado; pendiente de comprobar en el entorno publicado | Redirección HTTP→HTTPS salvo `/salud`; comprobación de HU02-CA05 descrita en [despliegue.md](despliegue.md). |
| S1-T09 | HU03 | Matriz de permisos y rechazo de solicitudes directas | Implementada y verificada en local | Políticas con nombre y denegación por defecto; [matriz-permisos.md](matriz-permisos.md); `MatrizAutorizacionTests` (45 casos ruta × perfil + control de rutas no declaradas). |
| S1-T10 | HU04 | Gestión de cuentas institucionales | Implementada y verificada en local | `/panel/admin/cuentas` (listado, alta, modificación); `CuentasInstitucionalesTests` CA01–CA05. |
| S1-T11 | HU04 | Sin contraseñas en claro; solo administrador | Implementada y verificada en local | `CuentasInstitucionalesTests` CA06 (solicitudes directas de los otros perfiles, sin cambios en los datos) y CA07 (hash V3 al crear y al cambiar la contraseña; nada en registros). |
| S1-T12 | HU06 | Estados del permiso de geolocalización | Pendiente | — |
| S1-T13 | HU07 | SOS con una activación, confirmado tras el servidor | Servidor implementado y verificado en local; app pendiente | `POST /api/sos` (201 solo tras guardar caso y evento en una transacción); `SosTests` HU07-CA02, CA04, CA05, CA06. CA01 y CA03 en la interfaz: app MAUI. |
| S1-T14 | HU07 | Idempotencia reutilizable | Implementada y verificada en local | `IdSolicitud` único por ciudadano (índice + 200 con el mismo SOS); 10 reintentos simultáneos → 1 SOS. Mecanismo descrito abajo para HU10, HU34 y HU41. |
| S1-T15 | HU08 | SOS con y sin ubicación actual | Parte de servidor verificada en local; captura en la app pendiente (SPIKE01) | `SosTests` HU08-CA01, CA02, CA03 (posición anterior a la activación descartada); ubicación inválida no bloquea el SOS. |
| S1-T16 | HU07, HU08 | Aceptación conjunta | Pendiente | — |
| S1-T17 | HU11 | Tiempo real y sección urgente (+ mapa base mínimo) | Implementada y verificada en local | Hub SignalR solo para operadores; aviso tras la confirmación; `/panel/central` con aviso visual y sonoro, estados de conexión y mapa Leaflet. `RecepcionSosTests` + verificación en Chrome. CA05 (RNF02) se mide en S1-T18. |
| S1-T18 | HU11 | Medición de RNF02 | Instrumentación y herramienta listas; **medición formal pendiente del entorno publicado** | Acuse de presentación del panel real, tabla de medición (apagada por defecto) y `Cad.PruebaCarga`. Ensayo local (no válido): 100/100 en ≤ 3 s, p95 178 ms. Ver [pruebas.md](pruebas.md). |
| S1-T19 | HU03 | Registro acumulativo de verificación por rol | En curso | Tabla B de [matriz-permisos.md](matriz-permisos.md): HU04 verificada; HU05–HU43 pendientes. HU03 no se acredita. |
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
| DP04 — «operador autorizado» | **Decidida por delegación (12/10/2026):** cualquier cuenta activa con rol Operador, mediante políticas con nombre propio para poder restringirla después. El administrador no recibe permisos de operador. |
| DP11 — ubicación del SOS | **Decidida por delegación (12/10/2026):** solo cuenta como actual una posición obtenida después de la activación; espera máxima configurable (10 s); si se agota, «Ubicación no disponible»; precisión guardada sin umbral de rechazo. |
| DP14 — SOS repetidos | **Decidida por delegación (12/10/2026):** cada activación crea un SOS nuevo, aunque exista otro pendiente; solo los reintentos con el mismo IdSolicitud se tratan como el mismo SOS. |
| Hosting del entorno de validación | **Decidido por delegación (13/10/2026):** Render Web Service (Docker) gratuito + Neon gratuito, USD 0. Render Starter solo con autorización (RNF10). Ver [ADR-06](decisiones/ADR-06-entorno-de-validacion.md). |

## Bloques ejecutados

| Bloque | Fecha | Tareas | Resultado |
|---|---|---|---|
| 1 — Esqueleto local | 12/10/2026 | S1-T01 (parcial) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 2/2 superadas. `GET /salud` → 200 `Healthy`. |
| 2 — Registro de ciudadano | 12/10/2026 | S1-T05, S1-T06 (HU01) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 22/22 superadas contra PostgreSQL 17 (Docker). Servidor real: 201 / 409 / 400 con motivos por campo. Sin contraseñas, correos ni teléfonos en los registros. |
| 3 — Inicio de sesión | 12/10/2026 | S1-T07 (HU02); DP02 | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 50/50 superadas. Servidor real: ciudadano obtiene JWT y consulta `/api/cuentas/yo`; contraseña incorrecta → 401; administrador sembrado entra a `/panel/admin`. Sin contraseñas, correos ni tokens en los registros. |
| 4 — Matriz de permisos | 12/10/2026 | S1-T09, S1-T19 (HU03); DP04 | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 102/102 superadas. Prueba de mutación: quitar una ruta de la matriz hace fallar el control de rutas no declaradas. |
| 5 — Cuentas institucionales | 12/10/2026 | S1-T10, S1-T11 (HU04) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 137/137 superadas. Prueba de mutación de la autorización del alta. Revisión en Chrome (1366×768): foco al resumen de errores, sin desbordes ni errores de consola. |
| 6 — Emisión de SOS (servidor) | 12/10/2026 | S1-T13, S1-T14, S1-T15 (servidor) (HU07, HU08); DP11, DP14 | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 161/161 superadas. Prueba de mutación: sin normalizar la hora a UTC, PostgreSQL rechaza la ubicación. |
| 7 — Recepción en la central | 13/10/2026 | S1-T17 (HU11) | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 176/176 superadas. Chrome (Playwright, 1366×768): SOS visible sin recargar, aviso visual y sonoro, marcador solo con ubicación, desconexión visible y SOS recuperado al reconectar, sin errores de consola. |
| 8 — Medición de RNF02 | 13/10/2026 | S1-T18 (HU11-CA05); E7 | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 188/188 superadas. Ensayo local con panel real en Chrome y 20 sesiones: 100/100 SOS presentados en ≤ 3 s (p50 45 ms, p95 178 ms, máx. 409 ms). **No válido para aceptar RNF02** (exige el entorno publicado). |
| 9 — Preparación del despliegue | 13/10/2026 | S1-T01, S1-T02, S1-T08 (TEC01); RNF09 | `dotnet build`: 0 advertencias, 0 errores. `dotnet test`: 201/201 superadas. Imagen Docker construida y **contenedor ejecutado** como en Render: `/salud` y `/salud/lista` OK, HTTP→HTTPS 307, HSTS, proceso sin root, registros 100 % JSON sin datos sensibles, claves de Data Protection en PostgreSQL. |

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

### Decisiones y observaciones del bloque 4

- **Denegación por defecto:** toda ruta exige sesión salvo declaración expresa. Consecuencia visible: `GET /` sin sesión ya no responde 404, sino que lleva a `/panel/ingresar`, y una ruta `/api` desconocida responde 401. La prueba de salud se actualizó en consecuencia.
- **`/panel/ingresar`:** solo redirige a su inicio a operador y administrador. Una sesión de ciudadano o sereno (por ejemplo, con JWT) ve el formulario, en lugar de ser enviada a una página que se le negaría.
- **Sin operaciones de escritura restringidas todavía:** la parte de HU03-CA03 «no se modifica ningún dato» se podrá verificar con HU04 (S1-T11), que añade las primeras.
- **Pendiente:** la comprobación de cuenta activa en cada solicitud depende de DP03 y no se aplica.

### Decisiones y observaciones del bloque 5

- **Datos de las cuentas institucionales** (criterio de DP01 aplicado al personal): nombres, apellidos, correo (identificador) y contraseña inicial obligatorios; teléfono opcional (DP01 lo exige al ciudadano por RF08).
- **Datos modificables:** nombres, apellidos, teléfono, **correo** (HU04-CA02 contempla modificar el identificador) y **contraseña** (HU04-CA07 contempla una contraseña modificada).
- **El rol se fija al crear la cuenta:** ningún criterio pide cambiarlo, y cambiarlo dejaría sesiones abiertas con el rol anterior (DP03). El formulario rechaza cualquier rol distinto de Operador o Sereno, también si se manipula la solicitud.
- **Solo cuentas institucionales:** la página de modificación responde 404 para cuentas de ciudadano o de administrador.
- **Reglas de datos compartidas** con HU01 en `ReglasCuenta` (mismos mensajes y validaciones).
- **Panel con sesión:** nuevo `_LayoutPanel` con encabezado (marca, navegación por rol, nombre, etiqueta de rol y «Salir»); central, administración y acceso denegado migrados. Se eliminó el layout antiguo con estilos en línea.
- **Pendiente:** la desactivación de cuentas corresponde a HU05 (Sprint 4).

### Decisiones y observaciones del bloque 6

- **Contrato de emisión** (`Cad.Contratos/Sos`): `IdSolicitud`, `ActivadoEn` y `Ubicacion` opcional. Sin categoría, descripción ni formulario (HU07-CA01).
- **Mecanismo de idempotencia reutilizable (S1-T14):** el cliente genera un `IdSolicitud` por acción del usuario y lo reutiliza en cada reintento; un índice único (titular, `IdSolicitud`) lo garantiza en la base de datos; la primera respuesta es 201 y las repeticiones 200 con el mismo recurso; ante una carrera, la violación de unicidad se traduce en «ya registrado». HU10, HU34 y HU41 deben seguir el mismo patrón (Plan de Arquitectura, 9.4).
- **DP11 aplicada en el servidor:** una posición con `ObtenidaEn` anterior a `ActivadoEn` no se asocia (HU08-CA03). Sin umbral de precisión.
- **Una ubicación inválida nunca bloquea el SOS:** se registra como «Ubicación no disponible» y la respuesta lo indica (`UbicacionDisponible: false`). Se registra una advertencia **sin coordenadas** (RNF07).
- **Restricciones en la base de datos:** coordenadas «todas o ninguna», rangos de latitud y longitud, precisión positiva, índice único de idempotencia y claves foráneas sin borrado en cascada.
- **Historial desde el primer momento:** cada SOS crea su evento `SosEmitido` en la misma transacción (base de HU27 y HU28).
- **Fuera de este bloque:** la notificación en tiempo real a la central (HU11, S1-T17) y la app (activación única, confirmación visible y captura de posición: S1-T12, S1-T15 y S1-T16).

### Decisiones y observaciones del bloque 7

- **Canal en tiempo real** (`/hubs/central`): SignalR con la cookie del panel, solo política `OperadorAutorizado`. El servidor asigna el grupo; el cliente no elige. Solo envía eventos: las escrituras siguen por HTTP (ADR-04).
- **Aviso posterior a la persistencia:** `EmitirSos` avisa después de confirmar la transacción y solo para un SOS nuevo (un reintento no repite el aviso). Si el aviso falla, el SOS queda registrado y confirmado al ciudadano; la central lo recupera al resincronizar. Verificado con una prueba que simula el fallo.
- **El evento solo lleva el identificador:** el panel vuelve a pedir la lista al servidor (fragmento generado por la página). La base de datos es la fuente de verdad y los datos personales no viajan por el canal.
- **Aviso sonoro:** generado con Web Audio (sin archivos), activado por el operador con un clic, por la política de reproducción automática de los navegadores (Plan de Arquitectura, SP-E). El aviso visual (resaltado «Nuevo», contador en el título y anuncio para lectores de pantalla) funciona siempre.
- **Estados:** conectando, conectado, reconectando (lista posiblemente desactualizada) y sin conexión con reintento cada 5 s; al reconectar se resincroniza. Sesión vencida: se indica que hay que volver a ingresar.
- **Orden de la sección urgente:** del SOS más reciente al más antiguo (ninguna HU fija el orden).
- **Mapa base mínimo (cláusula de HU11-CA01, opción aprobada):** Leaflet 1.9.4 con teselas de OpenStreetMap y su atribución; centro y zoom configurables (`Panel:Mapa`). Las peticiones de teselas revelan a OSM la zona visible del mapa, no los datos del SOS.
- **Librerías en `wwwroot/lib`** (sin CDN): Leaflet 1.9.4 (BSD-2-Clause) y @microsoft/signalr 10.0.11 (MIT), con sus licencias.
- **Pendiente:** medición de RNF02 (S1-T18), que requiere el acuse de presentación del panel y la herramienta de carga; verificación en Firefox (RNF05).

### Decisiones y observaciones del bloque 8

- **Qué se mide:** desde la aceptación del servidor (`Caso.RegistradoEn`) hasta el acuse del panel real cuando el SOS ya se muestra y se ha avisado; ambos instantes con el reloj del servidor. Un SOS sin acuse cuenta como incumplimiento. No es el tiempo de respuesta del sereno.
- **Instrumentación apagada por defecto** (`Medicion:Rnf02:Habilitada`): apagada no registra nada, el panel no envía acuses y el endpoint de resultados responde 404. Los resultados solo los lee el administrador.
- **Composición de las 20 sesiones (propuesta):** 19 sesiones de ciudadano que emiten SOS de forma concurrente y 1 panel real de operador. RNF02 no fija la composición; RNF03 (otra prueba) exige además serenos y acompañamientos.
- **Hosting (E7):** Render gratuito + Neon gratuito (ADR-06). La medición en ese entorno exige calentar antes el servicio.
- **Ensayo local:** útil para validar la herramienta y detectar problemas, pero no sustituye la medición en el entorno de pruebas que exige el requisito.

### Decisiones y observaciones del bloque 9

- **Detrás del proxy de Render** (`Despliegue:DetrasDeProxy=true`, solo en ese entorno): se aceptan `X-Forwarded-For` y `X-Forwarded-Proto`, se redirige HTTP→HTTPS (RNF09) y se envía HSTS. Excepción: `/salud` y `/salud/lista`, que Render consulta por HTTP interno y no llevan datos. Fuera de Render, las cabeceras reenviadas no se aceptan, para que nadie se haga pasar por HTTPS.
- **Sondeos:** `/salud` no toca la base de datos (no despierta Neon); `/salud/lista` sí la comprueba.
- **Claves de Data Protection en PostgreSQL:** las cookies del panel y los tokens antifalsificación siguen siendo válidos tras cada despliegue (el disco del contenedor es efímero). Se guardan sin cifrado adicional en reposo: aceptable con datos ficticios; revisarlo antes de cualquier uso real.
- **Npgsql sin GSSAPI:** no se usa Kerberos; sin desactivarlo, la imagen escribía errores de una librería ausente fuera del registro estructurado.
- **Producción:** registros en JSON y consultas SQL solo a nivel `Warning`.
- **CI** (`.github/workflows/ci.yml`): compilación y pruebas contra PostgreSQL 17 y construcción de la imagen en cada PR y en `main`. La base de CI es un contenedor efímero sin contraseña en el repositorio. Render despliega `main` solo si la CI pasa.
- **Pendiente del equipo:** crear las cuentas de Neon y Render y aplicar el Blueprint ([despliegue.md](despliegue.md)). Después: comprobaciones de RNF09/HU02-CA05 en el entorno, datos por rol (S1-T02), medición formal de RNF02 y demostración de extremo a extremo (S1-T21).
