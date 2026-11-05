# Demostración de la central

Dos guiones de Node 20+ (sin dependencias) que usan solo la API de la app y los formularios del panel, así que sirven igual en local y en Render.

- `sembrar.mjs`: 20 reportes en varios estados (recibidos, en revisión, admitidos, asignado, en camino, derivado y descartado) y 5 SOS (3 recién llegados, 1 clasificado y 1 asignado), alrededor del centro del mapa. Deja al menos un sereno disponible para mostrar el más cercano.
- `mover-serenos.mjs`: los serenos se mueven solos; patrullan y, si tienen un caso, van hacia él. Con `CAD_AVANZAR=1` marcan «En camino» y «En atención».

Las credenciales nunca se guardan en el repositorio: se pasan por variables de entorno.

```bash
CAD_URL=https://<servicio>.onrender.com CAD_OPERADOR="operador@correo:clave" CAD_SERENOS="s1@correo:clave;s2@correo:clave" node herramientas/demo/sembrar.mjs
CAD_URL=https://<servicio>.onrender.com CAD_SERENOS="s1@correo:clave;s2@correo:clave" CAD_CENTRO="-16.3988,-71.5369" node herramientas/demo/mover-serenos.mjs
```
