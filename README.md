# Sistema Integrado CAD — MVP académico

Prototipo académico (Ingeniería de Software II, USMP) de alertas SOS georreferenciadas y despacho para la seguridad ciudadana de una municipalidad tipo B. **No es un canal oficial de emergencias**: todos los datos son simulados.

Documentos de referencia (fuera del repositorio): Plan Scrum, Historias de usuario estructuradas y Plan de Arquitectura del Sistema Integrado CAD.

## Estado

Sprint 2 en curso. El avance por tarea está en [docs/trazabilidad-sprint2.md](docs/trazabilidad-sprint2.md) (Sprint 1: [docs/trazabilidad-sprint1.md](docs/trazabilidad-sprint1.md)).

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (fijado en `global.json`; acepta cualquier 10.0.x igual o superior a 10.0.100).
- Docker Desktop (con WSL 2) para PostgreSQL local.

App móvil: carga de trabajo `maui-android`, JDK 17 y Android SDK. Guía, comandos y verificación en [docs/app-movil.md](docs/app-movil.md).

## Estructura

```
Cad.slnx                     solución
src/Cad.Server/              Servidor CAD (ASP.NET Core 10): API, panel y tiempo real
  Modulos/Cuentas/           registro, inicio de sesión y cuentas (HU01–HU05)
  Persistencia/              CadDbContext y migraciones de EF Core
src/Cad.Contratos/           DTO compartidos entre servidor y app móvil
CadMovil.slnx                solución de la app móvil (Contratos + app + pruebas)
src/Cad.Movil/               app .NET MAUI (Android): ciudadano y sereno
tests/Cad.Movil.Tests/       pruebas de la lógica de la app (xUnit, sin emulador)
tests/Cad.Server.Tests/      pruebas (xUnit + WebApplicationFactory + PostgreSQL real)
docs/                        trazabilidad y documentación técnica
docker-compose.yml           PostgreSQL 17 local
```

## Base de datos local

1. Copia `.env.example` a `.env` y define `POSTGRES_PASSWORD` con un valor propio. `.env` no se sube al repositorio.
2. Levanta PostgreSQL (solo escucha en `127.0.0.1`):
   ```bash
   docker compose up -d
   ```
3. Guarda la cadena de conexión del servidor en user-secrets, que queda fuera del repositorio. Usa los mismos valores de tu `.env`:
   ```bash
   dotnet user-secrets set "ConnectionStrings:Cad" "Host=localhost;Port=5432;Database=cad;Username=cad;Password=TU_CONTRASEÑA" --project src/Cad.Server
   ```

## Secretos locales

El servidor no arranca si falta alguno de estos valores. Ninguno va en el repositorio: en desarrollo se guardan con user-secrets y en el entorno publicado como variables de entorno (`__` en lugar de `:`).

| Clave | Uso | Cómo generarla |
|---|---|---|
| `ConnectionStrings:Cad` | PostgreSQL | Ver «Base de datos local» |
| `Jwt:ClaveFirma` | Firma de los tokens de la app (HS256) | Base64 de 32 bytes aleatorios, p. ej. `node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"` |
| `Admin:Email` | Correo del administrador inicial (DP02) | Un correo ficticio, p. ej. `admin@cad.test` |
| `Admin:ClaveInicial` | Contraseña del administrador inicial | Mínimo 8 caracteres, con letra y número |

```bash
dotnet user-secrets set "Jwt:ClaveFirma" "VALOR" --project src/Cad.Server
```

El administrador inicial se crea al arrancar si no existe una cuenta con ese correo; si ya existe, no se modifica. Para consultar tus valores locales: `dotnet user-secrets list --project src/Cad.Server`.

El servidor aplica las migraciones al arrancar. Las pruebas usan otra base, `cad_pruebas`, que borran y recrean en cada ejecución, y leen las credenciales de `.env` (o de la variable `CAD_PRUEBAS_CONEXION`).

Para crear una migración nueva:
```bash
dotnet ef migrations add NombreMigracion --project src/Cad.Server --output-dir Persistencia/Migraciones
```
`dotnet-ef` es una herramienta local: ejecuta antes `dotnet tool restore`.

## Comandos

```bash
dotnet build Cad.slnx
dotnet test Cad.slnx
dotnet run --project src/Cad.Server --launch-profile http
```

Con el servidor en marcha, `http://localhost:5220/salud` responde `Healthy`. Este sondeo indica solo que el proceso está activo; no consulta la base de datos.

## API disponible

| Método y ruta | Rol | HU | Respuestas |
|---|---|---|---|
| `POST /api/cuentas/registro` | Anónimo | HU01 | 201 cuenta creada · 400 datos faltantes o inválidos (con el motivo por campo) · 409 correo ya registrado |
| `POST /api/auth/token` | Anónimo (ciudadano y sereno) | HU02 | 200 token JWT · 401 correo o contraseña incorrectos · 403 cuenta desactivada o rol que debe usar el panel |
| `GET /api/cuentas/yo` | Cualquier cuenta autenticada (JWT o cookie del panel) | HU02 | 200 id, nombres, apellidos y rol · 401 sin sesión |
| `POST /api/sos` | Ciudadano | HU07, HU08 | 201 SOS registrado (Pendiente, con o sin ubicación) · 200 mismo SOS ante un reintento con el mismo `idSolicitud` · 400 falta `idSolicitud` · 401/403 otros perfiles |
| `GET /api/sereno/estado` | Sereno | HU18 | 200 estado operativo, hora del último cambio e intervalo de envío de posiciones (DP15: 30 s, `Serenos:IntervaloPosicionesSegundos`) |
| `PUT /api/sereno/estado` | Sereno | HU18 | 200 estado actualizado · 400 activación sin permiso de ubicación declarado (DP16-a); desactivarse siempre está permitido (DP16-b) |
| `POST /api/sereno/posiciones` | Sereno | HU18 | 201 posición registrada · 200 reenvío de la misma posición · 409 sereno no operativo (no se registra) · 400 posición inválida |

## Panel web

`http://localhost:5220/panel/ingresar`: acceso de operadores y administradores. El administrador gestiona las cuentas de operador y sereno en `/panel/admin/cuentas` (HU04): listado, alta y modificación (cookie HttpOnly, SameSite Strict; Secure fuera de desarrollo). Cada rol llega a su interfaz: `/panel/central` (operador) o `/panel/admin` (administrador). El operador ve en `/panel/central` la sección urgente de SOS en tiempo real (HU11), con aviso visual y sonoro (actívalo con «Activar avisos sonoros») y el mapa operativo base. Desde cada SOS abre `/panel/central/sos/{id}` para ver el teléfono del ciudadano y registrar intentos de contacto, información del hecho y referencias de ubicación (HU12). La sección «Serenos» y el mismo mapa muestran el estado operativo, la disponibilidad y la última ubicación de cada sereno operativo, actualizados sin recargar (HU19). Con un SOS elegido, «Asignar al SOS seleccionado» en un sereno disponible asigna el caso tras confirmarlo; también desde el detalle del SOS (HU20). En el detalle, la sección «Clasificación» asigna al SOS una categoría del catálogo; hasta entonces la central lo muestra como «Tipo por determinar» (HU13). El sereno ve en la app su despacho (datos limitados por DP06) y recibe el aviso en tiempo real al asignárselo (HU21). Desde la app confirma que va en camino (HU22) y registra el inicio de atención, que también puede registrar el operador desde el detalle del SOS (HU23); la central refleja cada estado. Por último, el sereno o el operador cierran el caso con su tipificación final, el resultado público y notas internas separadas, y el sereno vuelve a figurar como disponible (HU24). Las cuentas de ciudadano y sereno se rechazan en el panel y las de operador y administrador en `/api/auth/token`, según la Tabla 3 del Plan de Arquitectura.

## Despliegue

Entorno de validación en Render + Neon (gratuito, [ADR-06](docs/decisiones/ADR-06-entorno-de-validacion.md)): `Dockerfile`, `render.yaml` y guía paso a paso en [docs/despliegue.md](docs/despliegue.md). La CI de GitHub Actions compila, prueba y construye la imagen en cada PR.

## Pruebas no funcionales

Medición de RNF02 con `tests/Cad.PruebaCarga`: procedimiento y resultados en [docs/pruebas.md](docs/pruebas.md).

## Librerías de terceros

Incluidas en `src/Cad.Server/wwwroot/lib` (sin CDN): Leaflet 1.9.4 (BSD-2-Clause) y @microsoft/signalr 10.0.11 (MIT), con sus licencias. El mapa usa teselas de OpenStreetMap (requiere conexión a internet y muestra su atribución).

## Reglas del repositorio

- Ningún secreto, contraseña ni cadena de conexión en el repositorio: se usan variables de entorno y un `.env` local (ignorado por Git).
- No se registran en los logs contraseñas, tokens, coordenadas ni datos personales.
- La autorización se aplica en el servidor, no solo en la interfaz. Toda ruta exige sesión salvo que se declare pública; cada ruta nueva usa una política de `Compartido/Autorizacion.cs` y se añade a la matriz de [docs/matriz-permisos.md](docs/matriz-permisos.md) (la prueba `MatrizAutorizacionTests` falla si falta).
