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

Después busca cada valor sensible en **todas** las entradas capturadas, en **todos los niveles** (incluidos Debug y Trace). Es más estricto que la configuración real, que no registra esos niveles para el framework.

| Categoría | Valores buscados | Hallazgos |
|---|---|---|
| Contraseñas | De la ciudadana, del operador (inicial y nueva), del administrador y las de los intentos fallidos | 0 |
| Tokens y cookies | JWT emitido, cookies `cad.panel` del administrador y del operador, sellos de seguridad de Identity y cualquier texto con forma de JWT | 0 |
| Datos personales | Correos, teléfono y apellidos ficticios | 0 |
| Coordenadas | Latitud y longitud del SOS, con punto y con coma decimal | 0 |
| Secretos del servidor | Clave de firma JWT, contraseña de la base de datos y cualquier `Password=` | 0 |

**La prueba detecta fugas reales:** al añadir temporalmente un registro de nivel Debug con las coordenadas del SOS, falla y las señala («coordenadas: -12.07…»). Después se restauró el código.

## Otras comprobaciones del sprint

- **Contenedor de producción** (bloque 9): registros 100 % JSON, sin contraseña de la base de datos, contraseña del administrador, cadenas de conexión ni correos.
- **Por historia:** HU01-CA06, HU02-CA04, HU04-CA07 (contraseñas) y la prueba de RNF07 de HU07/HU08 (coordenadas), en sus respectivas pruebas.

## Fuera del alcance de esta revisión

- **«Datos enviados fuera del canal cifrado»:** en local el servidor usa HTTP. En el entorno publicado, HTTP se redirige a HTTPS (salvo `/salud`), con HSTS y cookie `Secure` (bloque 9). La comprobación final de RNF09 se hará en Render ([despliegue.md](despliegue.md), sección 3).
- **Registros del proveedor** (Render y Neon): se revisarán en el entorno publicado.
