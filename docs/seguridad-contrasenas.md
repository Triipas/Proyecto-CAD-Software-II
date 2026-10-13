# RNF01 — Almacenamiento de contraseñas

Requisito (Plan Scrum, RNF01): almacenar contraseñas con un algoritmo de hash específico para contraseñas, con configuración documentada, sin texto plano ni cifrado reversible.

## Configuración

| Elemento | Valor |
|---|---|
| Implementación | `PasswordHasher<Usuario>` de ASP.NET Core Identity 10 (`Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12) |
| Formato | Identity V3 |
| Algoritmo | PBKDF2 con HMAC-SHA512 |
| Iteraciones | 100 000 (valor por defecto de Identity V3; no se modifica) |
| Sal | 128 bits, aleatoria por contraseña |
| Subclave derivada | 256 bits |
| Almacenamiento | Columna `PasswordHash` de `AspNetUsers`, en Base64: `0x01` · PRF · iteraciones · longitud de la sal · sal · subclave |

El hash no es reversible: para verificar un inicio de sesión se vuelve a derivar la subclave con la sal guardada y se comparan los resultados en tiempo constante.

## Reglas de contraseña (DP01, propuesta validada el 12/10/2026)

Entre 8 y 128 caracteres, con al menos una letra y un número. Se validan en el servidor (`RegistrarCiudadano`) y se reflejan en las opciones de Identity (`ModuloCuentas`).

## Registros

- No se registran cuerpos de solicitud ni de respuesta.
- `EnableSensitiveDataLogging` de EF Core permanece desactivado: los parámetros SQL aparecen como `?`.

## Verificación automatizada (HU01-CA06)

`RegistroCiudadanoTests.CA06_*` registra una cuenta contra PostgreSQL real y comprueba:

- que el hash no contiene la contraseña y respeta el formato V3, con PRF HMAC-SHA512, 100 000 iteraciones, sal de 16 bytes y longitud total de 61 bytes;
- que ninguna columna de la fila de `AspNetUsers` contiene la contraseña en claro;
- que ninguna entrada de registro del servidor, en todos los niveles, contiene la contraseña, el correo ni el teléfono.
