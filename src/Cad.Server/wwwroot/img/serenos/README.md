# Camioneta de serenazgo · verde y gris

Sprite generado con Imagegen integrado a partir de la opción A aprobada: camioneta compacta verde claro, volumen 3D sencillo y
materiales sólidos sin texturas realistas. El PNG se conserva con su transparencia original.

`camioneta-verde-direcciones.png` representa al sereno disponible. `camioneta-gris-direcciones.png` es la misma camioneta
en grises para el estado no disponible (con caso o fuera de servicio). Ambos atlas conservan las mismas posiciones y
orientaciones; el estado gris usa su propio sprite. El atlas azul original se conserva como referencia de procedencia;
los mapas utilizan el verde para los disponibles.

La barra de la sirena cruza el ancho de la cabina en las ocho perspectivas. Se corrigieron las dos vistas traseras
diagonales donde la barra aparecía en el sentido longitudinal de la camioneta.

El atlas tiene cuatro columnas y dos filas: **N, NE, E, SE / S, SO, O, NO**. Cada celda presenta una vista diferente del
vehículo; no se rota una ilustración plana. `marcadores.js` selecciona la celda y `panel.css` dibuja el marcador de 56 × 56 px.

En la central, el rumbo se calcula a partir de posiciones GPS sucesivas, **con o sin caso o ruta asignada**. La app comparte
ubicaciones mientras el sereno está operativo (por defecto cada 15 s), independientemente de los despachos.

Se exige un desplazamiento de al menos 4 m y mayor que la suma de las precisiones de ambas lecturas; una banda de 5° al
cambiar de sector reduce la alternancia. Al detenerse, cambiar de disponibilidad o llegar al caso se conserva el rumbo.
La primera posición solo permite dibujar la camioneta: se usa la vista SE de presentación y el globo dice «Sin rumbo
confirmado» hasta observar un desplazamiento suficiente. El globo diferencia esta vista del último rumbo observado.

Las lecturas repetidas o atrasadas no cambian el rumbo. Si pasan más de 120 s sin muestras, la ubicación que llega pasa a
ser la nueva referencia: se conserva el último rumbo conocido hasta el siguiente desplazamiento, sin inferir un trayecto
recto durante la desconexión. No se extrapola movimiento, se inventa un destino ni se solicitan rutas para el patrullaje libre.
El minimapa estático puede usar el primer tramo de la ruta del sereno asignado.

El escudo de la lista de serenos permanece sin cambios. En el mapa, un visto circular identifica la disponibilidad y un
guion sobre un cuadrado identifica la falta de disponibilidad; el título y el globo conservan su estado en texto.

Los prompts completos y la procedencia están en los archivos `camioneta-verde-direcciones.provenance.json` y
`camioneta-gris-direcciones.provenance.json`.

Para revisar el sprite sin cuentas ni base de datos: `node herramientas/demo/probar-camioneta.mjs` desde la raíz y abrir
<http://localhost:5251>. La prueba utiliza los mismos archivos del sistema y solo simula posiciones en el navegador.
Por defecto el recorrido está oculto para mostrar cómo funciona un sereno sin ruta; se puede mostrar desde los controles.
