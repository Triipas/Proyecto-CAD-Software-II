# Fase 3 · Revisión final de impeccable

Revisión de cierre del panel web contra el contrato de dirección «Mapa protagonista» (`.impeccable/surfaces/src-cad-server-panel-pages-central-index-cshtml.md`, `seed:8a6fb6d8fa5023a4`, verificado con `concept-seed --check-seed`). Construcción guiada por código (superficie de modo Operate), sin comp aprobado.

La revisión y la documentación se hicieron en el hilo principal con las guías `degraded/finish-reviewer.md` y `degraded/documenter.md` de impeccable, porque en esta sesión no se lanzan subagentes sin pedirlo.

Evidencia: capturas de la central con teselas reales en `.impeccable/review/` (`desktop.png` a 1366 × 768, `mobile.png` y `mobile-inicio.png` a 390 × 844), además de las 54 referencias de la revisión visual automatizada (`tests/Cad.Visual`).

## Primera revisión: `fix`

| Elemento del contrato | Estado |
|---|---|
| Mapa a sangre con cola y serenos flotantes | coincide |
| Barra superior marino con secciones y persona | coincide |
| Indicadores en vivo en la barra (SOS, por asignar, serenos libres, acompañamientos) | faltaba |
| Pestañas en la cola (Todo · SOS · Reportes · Acompañamientos) | faltaba |
| Línea de despacho al sereno más cercano con distancia (momento memorable) | coincide |
| Asignación con diálogo de confirmación | coincide |
| Mapa en azul grisáceo apagado (OWN-WORLD) | contradicho: teselas OSM a todo color |
| Chips con tono, icono y texto; rojo solo en SOS; serenos como escudos marino | coincide |
| Public Sans con cifras tabulares | coincide |
| Leyenda en chips entre los paneles | coincide |

Correcciones materiales, en orden:

1. Apagar las teselas a azul grisáceo en todos los mapas del panel (`--mapa-filtro-teselas`).
2. Indicadores en vivo en la barra de la central.
3. Filtro de la cola (Todo · SOS · Reportes). Los acompañamientos quedan fuera porque no se asignan ni generan despacho (verdad del producto); tienen su indicador en la barra y su sección.
4. El inicio del administrador era la plantilla de cifras grandes con tarjetas de icono: pasa a una línea de resumen con la lista de cuentas.
5. Las cifras del mapa de concentración eran la misma plantilla: pasan a una franja dividida.
6. Superficies del navegador con la paleta: selección, cursor de texto y barras de desplazamiento.

A conservar al corregir: el mapa a sangre, la línea de despacho y el vuelo al caso.

## Veredicto tras las correcciones: `ship`

| Corrección | Resultado |
|---|---|
| 1. Mapa apagado | resuelta: teselas en azul grisáceo; las calles siguen legibles y los casos resaltan |
| 2. Indicadores en la barra | resuelta: «SOS» en rojo cuando hay y «por asignar» a 1366 px; los cuatro desde 1400 px; solo «SOS» a 390 px; laten una vez al cambiar |
| 3. Filtro de la cola | resuelta |
| 4. Inicio del administrador | resuelta |
| 5. Cifras del mapa de concentración | resuelta |
| 6. Superficies del navegador | resuelta en el código; no se aprecia en las capturas |

Adaptación introducida por las correcciones: en la central, por debajo de 760 px, la barra oculta las iniciales de la persona para que quepa el indicador de SOS (el ancho a 390 px lo obliga). El veredicto `ship` cubre las seis correcciones puntuadas.

## Documentación

`DESIGN.md` (raíz) y `.impeccable/design.json` describen el sistema construido, con «El tablero de embarque» como estrella polar.

Deriva previa que no se corrige aquí: `tokens.css` declara dos veces `--color-reporte-borde` y `--color-acompanamiento-borde`, con el mismo valor.

La regla `.sobretitulo` se eliminó de `panel.css`: ninguna página la usa y es un patrón que el sistema rechaza.
