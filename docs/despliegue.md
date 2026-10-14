# Despliegue del entorno de validación (TEC01)

Entorno decidido en [ADR-06](decisiones/ADR-06-entorno-de-validacion.md): **Render Web Service (Docker) gratuito + Neon PostgreSQL gratuito**, misma región (EE. UU. Este). **Costo: USD 0.** No contratar planes de pago sin autorización expresa del equipo.

> Las cuentas, la conexión de GitHub con Render y los secretos los gestiona el equipo. Ningún secreto se guarda en el repositorio.

## 1. Base de datos en Neon

1. Crear una cuenta en neon.tech y un proyecto `cad-validacion`, región **AWS US East (N. Virginia)**, PostgreSQL 17.
2. Copiar la cadena de conexión que muestra Neon (formato `postgresql://USUARIO:CLAVE@HOST/BASE?sslmode=require`).
3. Convertirla al **formato de Npgsql** (Npgsql no acepta la forma `postgresql://`):
   ```
   Host=HOST;Port=5432;Database=BASE;Username=USUARIO;Password=CLAVE;SSL Mode=Require
   ```
   No pegarla en ningún archivo del repositorio ni en el chat.

## 2. Servicio en Render

1. Crear una cuenta en render.com (plan Hobby gratuito) y conectar la cuenta de GitHub con acceso al repositorio `Proyecto-CAD-Software-II`.
2. **New → Blueprint** y elegir el repositorio: Render lee `render.yaml` y propone el servicio `cad-servidor` (Docker, plan **Free**, región Virginia, comprobación de salud `/salud`).
3. Introducir los valores marcados como secretos (`sync: false`):

   | Variable | Valor |
   |---|---|
   | `ConnectionStrings__Cad` | La cadena de Neon en formato Npgsql (paso 1.3) |
   | `Admin__Email` | Correo ficticio del administrador inicial, p. ej. `admin@cad-validacion.test` |
   | `Admin__ClaveInicial` | Contraseña del administrador inicial (mínimo 8, con letra y número) |

   `Jwt__ClaveFirma` la genera Render (256 bits aleatorios). El resto de variables ya vienen en `render.yaml`.
4. **Apply**. La primera compilación de la imagen tarda unos minutos; al arrancar, el servidor aplica las migraciones en Neon y crea el administrador inicial.
5. Despliegues siguientes: automáticos al fusionar en `main`, **solo si la CI de GitHub pasa** (`autoDeployTrigger: checksPass`).

## 3. Comprobaciones tras el despliegue (S1-T01, S1-T02, S1-T08, RNF09)

Sustituir `SERVICIO` por la URL de Render (`https://cad-servidor-xxxx.onrender.com`).

| Comprobación | Cómo | Resultado esperado |
|---|---|---|
| Proceso vivo | `https://SERVICIO/salud` | `Healthy` |
| Base de datos accesible | `https://SERVICIO/salud/lista` | `Healthy` |
| RNF09: sin HTTP en claro | `http://SERVICIO/panel/ingresar` | Redirección a `https://` (301 del borde de Render; 307 del servidor si la petición llega sin cifrar) |
| RNF09: HSTS | Herramientas del navegador → respuesta de `/panel/ingresar` | Cabecera `Strict-Transport-Security` |
| RNF09: tiempo real cifrado | Herramientas del navegador → red, en `/panel/central` | Conexión `wss://SERVICIO/hubs/central` |
| HU02-CA05: credenciales solo cifradas | Ingresar como administrador; ver la petición `POST /panel/ingresar` | Va por `https://`; cookies `cad.panel` y `.AspNetCore.Antiforgery.*` con `Secure`, `HttpOnly`, `SameSite=Strict` |
| S1-T02: datos de prueba por rol | Como administrador, crear en «Cuentas institucionales» un operador y un sereno ficticios; registrar un ciudadano ficticio con `POST /api/cuentas/registro` | Los cuatro roles entran en su canal: panel (operador, administrador) y `POST /api/auth/token` (ciudadano, sereno) |
| Flujo de extremo a extremo (S1-T21) | Operador en `/panel/central`; ciudadano emite `POST /api/sos` | El SOS aparece sin recargar, con aviso visual y sonoro |

Registrar fecha, URL y resultado de cada comprobación en `docs/trazabilidad-sprint1.md`.

### Resultados en `https://cad-servidor.onrender.com` (13/10/2026)

| Comprobación | Resultado |
|---|---|
| Blueprint aplicado desde `main` (`73bffbe`, PR #5) | Servicio `cad-servidor` creado en Render (Free, Virginia) con Neon en `us-east-1` |
| `/salud` y `/salud/lista` | 200 `Healthy`: proceso vivo, migraciones aplicadas en Neon |
| `http://…/panel/ingresar` | 301 a `https://…/panel/ingresar` |
| HSTS | `Strict-Transport-Security: max-age=2592000` |
| Ingreso del administrador sembrado | Correcto (verificado por el equipo) |
| Cookie antifalsificación | Sin `Secure` en ese despliegue; corregido después (misma política que `cad.panel`) y pendiente de comprobar tras el siguiente despliegue |
| Datos por rol (S1-T02), tiempo real `wss://`, extremo a extremo (S1-T21) | Pendientes: requieren iniciar sesión y crear cuentas en el entorno publicado (equipo) |

## 4. Antes de una demostración o medición

- El plan gratuito **se suspende tras 15 min sin tráfico** y tarda alrededor de 1 min en arrancar; Neon escala a cero tras 5 min. **Calentar** el servicio abriendo `/salud/lista` y el panel hasta ver «Conectado en tiempo real».
- **Medición de RNF02** ([pruebas.md](pruebas.md)): en Render → Environment, poner `Medicion__Rnf02__Habilitada=true` (redespliega), medir con el servicio caliente y **volver a `false`** al terminar. Si hubo una suspensión o un reinicio durante la medición, repetirla.

## 5. Volver a una versión anterior

En Render → el servicio → **Events**, elegir un despliegue anterior y **Rollback**. Las migraciones no se revierten automáticamente: si una versión nueva añadió una migración, revisar antes la compatibilidad.

## Imagen local (opcional)

Para reproducir el contenedor en un equipo con Docker (PostgreSQL de `docker compose`):

```bash
docker build -t cad-servidor:local .
```

Ejecutarlo con las mismas variables que `render.yaml` (`ASPNETCORE_ENVIRONMENT=Production`, `Despliegue__DetrasDeProxy=true`, `ASPNETCORE_HTTPS_PORT=443`, `ConnectionStrings__Cad` con `Host=host.docker.internal`, `Jwt__ClaveFirma`, `Admin__Email`, `Admin__ClaveInicial`) y publicar el puerto 8080. Las peticiones directas por HTTP se redirigen a HTTPS salvo `/salud`; para simular el proxy, añadir la cabecera `X-Forwarded-Proto: https`.
