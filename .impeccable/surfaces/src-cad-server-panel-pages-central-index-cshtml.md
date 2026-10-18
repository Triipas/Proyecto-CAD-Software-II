---
version: 1
slug: "src-cad-server-panel-pages-central-index-cshtml"
primary_target: "src/Cad.Server/Panel/Pages/Central/Index.cshtml"
related_targets: ["docs/rediseno/prototipos/central.html"]
---

# Central de monitoreo

Modo: Operate. Superficie: la central del operador (`/panel/central`) y, por extensión, el armazón del panel web.

- **Quién y qué:** el operador de central, en turnos largos y bajo presión cuando entra un SOS. Su trabajo: ver lo urgente al instante, verificar, asignar el sereno adecuado y seguir cada caso hasta su cierre.
- **Contenido real:** SOS pendientes (0 a 6 a la vez), reportes admitidos por asignar, acompañamientos activos y casos urgentes derivados de ellos, serenos en turno con su posición y disponibilidad, y conexión en tiempo real.
- **Estados que importan:** turno tranquilo (sin SOS), normal (1 SOS y 1 reporte) y saturado (5 o más SOS y un caso urgente de acompañamiento); sin serenos disponibles; conexión perdida y recuperada; SOS sin ubicación.
- **Dirección elegida:** «Mapa protagonista» (estructura 2 de 7, ronda del 18/10/2026). Momento memorable: al elegir un SOS, una línea de despacho une el caso con el sereno disponible más cercano y muestra la distancia.
- **No se toca:** los atributos `data-*`, los `id` y los textos que verifican las pruebas; `central.js` sigue siendo la fuente del tiempo real.
- **Pendiente:** cómo plegar los paneles a 1366 × 768 sin perder la cola; si la barra superior sustituye a la lateral prevista en D1 en todo el panel.

## Direction contract

THESIS: La central es el mapa. La ciudad ocupa toda la pantalla y el trabajo flota encima: la cola a la izquierda y los serenos a la derecha. Se rechaza el tablero de tarjetas apiladas en una columna, donde el mapa es un recuadro más.

OWN-WORLD: Barra superior en `color-marino` con secciones e indicadores en vivo. Mapa en tonos azul grisáceo apagados, a sangre. Paneles blancos con `radio-dialogo` y `sombra-3`. Chips de estado con tono, icono y texto. El rojo solo marca el SOS. Los serenos son escudos marino con anillo blanco. Public Sans con cifras tabulares.

STORY: El operador ve primero el número de SOS en la barra y el SOS resaltado en la cola. Lo elige, la línea de despacho le muestra al sereno más cercano y asigna desde la misma tarjeta, con un diálogo de confirmación. Cree que nada urgente se le escapa porque lo que cambia queda resaltado hasta que lo atiende.

FIRST VIEWPORT: Barra superior de 60 px: marca, cinco secciones, indicadores (SOS, por asignar, serenos libres, acompañamientos) y la persona. Debajo, el mapa en todo el ancho. Panel de cola de 372 px a la izquierda, a toda la altura, con pestañas (Todo, SOS, Reportes, Acompañamientos), y la acción principal en la tarjeta seleccionada. Panel de serenos de 300 px arriba a la derecha. Leyenda en chips abajo, entre los dos paneles.

FORM: Mapa protagonista, estructura 2 de 7 de la lista ordenada. Elevada con la disciplina del tablero de embarque: lo que cambia se mueve en su sitio y queda resaltado hasta que alguien lo atiende. seed:8a6fb6d8fa5023a4

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
