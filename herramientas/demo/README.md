# Demostración de la central

Dos guiones de Node 20+ (sin dependencias) que usan solo la API de la app y los formularios del panel, así que sirven igual en local y en Render.

- `sembrar.mjs`: 20 reportes en varios estados (recibidos, en revisión, admitidos, asignado, en camino, derivado y descartado) y 5 SOS (3 recién llegados, 1 clasificado y 1 asignado), alrededor del centro del mapa. Deja al menos un sereno disponible para mostrar el más cercano.
- `mover-serenos.mjs`: los serenos se mueven solos; patrullan y, si tienen un caso, van hacia él. Con `CAD_AVANZAR=1` marcan «En camino» y «En atención».

Las credenciales nunca se guardan: si no vienen en variables de entorno, el guion las pide en la terminal (la contraseña no se ve al escribirla) y solo quedan en memoria mientras corre.

En una sola orden (PowerShell), siembra y luego deja a los serenos moviéndose hasta Ctrl+C:

```powershell
$env:CAD_URL = 'https://cad-servidor.onrender.com'; $env:CAD_MOVER = '1'; $env:CAD_AVANZAR = '1'; node herramientas/demo/sembrar.mjs
```

O por separado, con variables de entorno:

```bash
CAD_URL=https://<servicio>.onrender.com CAD_OPERADOR="operador@correo:clave" CAD_SERENOS="s1@correo:clave;s2@correo:clave" node herramientas/demo/sembrar.mjs
CAD_URL=https://<servicio>.onrender.com CAD_SERENOS="s1@correo:clave;s2@correo:clave" CAD_CENTRO="-16.3988,-71.5369" node herramientas/demo/mover-serenos.mjs
```

## Dejar la central limpia

`limpiar.mjs` borra **todos** los SOS y reportes (con su historial, asignaciones, revisiones, fotos y notificaciones) y el acompañamiento, y deja a los serenos fuera de servicio; conserva las cuentas, las categorías y los ajustes de la IA. Pide la cadena de conexión de PostgreSQL (la de Neon, sin mostrarla), enseña cuántos casos hay y espera que escribas `BORRAR`. Todo va en una transacción: si algo falla, no se borra nada. Con `CAD_PROBAR=1` hace lo mismo y lo deshace al final.

```powershell
node herramientas/demo/limpiar.mjs
```

