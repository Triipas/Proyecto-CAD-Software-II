# Demostración de la central

## Prueba local de la camioneta verde

`node herramientas/demo/probar-camioneta.mjs` abre una vista en `http://localhost:5251` con el sprite del sistema sobre el
mapa: recorrido simulado, pausa, ocho direcciones y cambio de disponibilidad. No necesita cuentas ni modifica la base de
datos. Usa los mismos `marcadores.js`, `movimiento.js`, estilos y atlas que la central. Ctrl+C cierra la vista de prueba.
La vista inicia sin ruta dibujada: el patrullaje se orienta con las posiciones GPS simuladas. «Mostrar recorrido» permite
comparar el mismo movimiento con su trazo visible; no cambia el cálculo del rumbo.

## Datos frescos para la presentación (una sola orden)

`presentacion.mjs` carga el [dataset de J. L. Bustamante y Rivero](../dataset-jlbyr/README.md) con el periodo terminando ahora
(unos 5000 casos para el dashboard, la analítica, el mapa de concentración y la proyección; al cargar quedan 5 SOS y 20 reportes
abiertos en distintas etapas) y después deja a los serenos en servicio moviéndose desde su última posición, con «En camino» y
«En atención» automáticos, hasta Ctrl+C. Vacía la base de datos antes de cargar (conserva el administrador). Necesita .NET y Node 20+.

```powershell
# Render (Neon): repite el host de Neon para confirmar que se vacía esa base
node herramientas/demo/presentacion.mjs --url https://<servicio>.onrender.com --conexion ~/.cad/neon.env --confirmar <host-de-neon>
# Local: la base de los user-secrets del servidor y el servidor en http://localhost:5220
node herramientas/demo/presentacion.mjs
```

`--solo-cargar` no mueve a los serenos; `--solo-mover` vuelve a moverlos sin recargar (usa la lista de la última carga,
`~/.cad/serenos-en-servicio.json`). La contraseña del personal sale de `~/.cad/cargador-credenciales.txt` y no se muestra.
Conviene lanzarlo 5 a 15 minutos antes de presentar: los casos abiertos envejecen desde la carga.

## Guiones sueltos

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

