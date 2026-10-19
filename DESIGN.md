---
name: Sistema Integrado CAD
description: Panel de central y app de seguridad ciudadana, azul formal, con el mapa como protagonista.
colors:
  marino-noche: "#0B1F44"
  marino-elevado: "#132C5C"
  marino-hundido: "#06142E"
  azul-accion: "#1D4ED8"
  azul-accion-hover: "#1A3FB0"
  azul-accion-activo: "#16348F"
  azul-tenue: "#E6EEFD"
  azul-tenue-fuerte: "#CFDDFB"
  celeste-senal: "#5CC8FF"
  fondo-niebla: "#F3F6FB"
  superficie: "#FFFFFF"
  superficie-hundida: "#EDF2F9"
  tinta: "#0D1B33"
  tinta-secundaria: "#475A75"
  tinta-terciaria: "#5C6E88"
  tinta-sobre-marino: "#D5E0F0"
  borde: "#D9E2EE"
  borde-fuerte: "#C3CFDF"
  borde-control: "#7487A2"
  rojo-sos: "#B42318"
  rojo-sos-fondo: "#FEF3F2"
  violeta-reporte: "#6D28D9"
  violeta-reporte-fondo: "#F4EFFE"
  verde-azulado-acompanamiento: "#0F766E"
  verde-azulado-fondo: "#E8F6F4"
  ambar-advertencia: "#93370D"
  ambar-advertencia-fondo: "#FFF8EB"
  verde-exito: "#05603A"
  verde-exito-fondo: "#ECFDF3"
  pizarra-neutro: "#3F4E63"
  pizarra-neutro-fondo: "#EEF2F7"
  sereno-escudo: "#1D3B78"
typography:
  display:
    fontFamily: "Public Sans, Segoe UI, system-ui, sans-serif"
    fontSize: "2.25rem"
    fontWeight: 700
    lineHeight: 1.1
    fontFeature: "tnum"
  headline:
    fontFamily: "Public Sans, Segoe UI, system-ui, sans-serif"
    fontSize: "1.75rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "-0.015em"
  title:
    fontFamily: "Public Sans, Segoe UI, system-ui, sans-serif"
    fontSize: "1.125rem"
    fontWeight: 600
    lineHeight: 1.35
  body:
    fontFamily: "Public Sans, Segoe UI, system-ui, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "Public Sans, Segoe UI, system-ui, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 600
    lineHeight: 1.125rem
    letterSpacing: "0.01em"
rounded:
  pequeno: "6px"
  control: "8px"
  superficie: "12px"
  dialogo: "16px"
  pildora: "999px"
spacing:
  "1": "4px"
  "2": "8px"
  "3": "12px"
  "4": "16px"
  "5": "20px"
  "6": "24px"
  "8": "32px"
  "10": "40px"
  "12": "48px"
  "16": "64px"
components:
  button-primary:
    backgroundColor: "{colors.azul-accion}"
    textColor: "{colors.superficie}"
    rounded: "{rounded.control}"
    padding: "8px 24px"
    height: "44px"
  button-primary-hover:
    backgroundColor: "{colors.azul-accion-hover}"
  button-secondary:
    backgroundColor: "{colors.superficie}"
    textColor: "{colors.marino-noche}"
    rounded: "{rounded.control}"
    padding: "8px 24px"
    height: "44px"
  button-secondary-hover:
    backgroundColor: "{colors.azul-tenue}"
  button-danger:
    backgroundColor: "{colors.rojo-sos}"
    textColor: "{colors.superficie}"
    rounded: "{rounded.control}"
    padding: "8px 24px"
    height: "44px"
  input:
    backgroundColor: "{colors.superficie}"
    textColor: "{colors.tinta}"
    rounded: "{rounded.control}"
    padding: "8px 12px"
    height: "44px"
  chip-urgente:
    backgroundColor: "{colors.rojo-sos-fondo}"
    textColor: "{colors.rojo-sos}"
    rounded: "{rounded.pildora}"
    padding: "2px 10px 2px 8px"
  chip-progreso:
    backgroundColor: "{colors.azul-tenue}"
    textColor: "{colors.azul-accion}"
    rounded: "{rounded.pildora}"
    padding: "2px 10px 2px 8px"
  chip-exito:
    backgroundColor: "{colors.verde-exito-fondo}"
    textColor: "{colors.verde-exito}"
    rounded: "{rounded.pildora}"
    padding: "2px 10px 2px 8px"
  card:
    backgroundColor: "{colors.superficie}"
    rounded: "{rounded.superficie}"
    padding: "32px"
  floating-panel:
    backgroundColor: "{colors.superficie}"
    rounded: "{rounded.dialogo}"
  top-bar:
    backgroundColor: "{colors.marino-noche}"
    textColor: "{colors.tinta-sobre-marino}"
    height: "60px"
---

# Design System: Sistema Integrado CAD

## Overview

**Creative North Star: "El tablero de embarque"**

El sistema sirve a una central de seguridad ciudadana: un operador en turnos largos, bajo presión cuando entra un SOS, y un administrador que mantiene las cuentas. Como en el tablero de un aeropuerto, lo que cambia se mueve en su sitio y queda resaltado hasta que alguien lo atiende; lo demás guarda silencio. La estructura es azul marino, la acción es un azul limpio y el resto es papel frío. El color de estado aparece solo donde hay un caso: rojo para el SOS, violeta para el reporte, verde azulado para el acompañamiento.

La central es el mapa. La ciudad, en azul grisáceo apagado, ocupa la pantalla y el trabajo flota encima en paneles blancos. Las pantallas de detalle siguen un mismo patrón: una línea de progreso del caso, una sola «Siguiente acción» destacada, las demás acciones plegadas y una columna de contexto. Es un sistema denso pero legible: cifras tabulares, chips con icono y texto, y nunca un color sin palabra.

Rechazos confirmados: el tablero de tarjetas apiladas donde el mapa es un recuadro más; los rótulos sobre el título; las franjas de color laterales; las tarjetas de cifras sueltas como plantilla de inicio.

**Key Characteristics:**
- Barra superior marino de 60 px con secciones, indicadores en vivo y la persona.
- Mapa a sangre con teselas apagadas; marcadores con forma por entidad.
- Detalle guiado por «Siguiente acción» con línea de progreso.
- Chips de estado con tono, icono y texto literal del estado.
- Movimiento con propósito: vuelo al caso, anillos de llegada, pulso solo en el SOS sin abrir.

## Colors

Paleta fría y formal: estructura marino, acción azul, papel niebla; los colores de dominio se reservan para los casos.

### Primary
- **Azul de acción** (#1D4ED8): botón principal, enlaces, foco, línea de despacho, paso actual del progreso. Blanco encima 6.70:1. Hover #1A3FB0, presionado #16348F.
- **Marino noche** (#0B1F44): barra superior, títulos, cifras. Es la estructura: no es un color de acción.

### Secondary
- **Celeste señal** (#5CC8FF): solo sobre marino (foco y subrayado de la sección activa en la barra). Nunca como texto sobre fondo claro.

### Tertiary (dominio)
- **Rojo SOS** (#B42318): exclusivo del SOS y del caso urgente: etiqueta, marcador, indicador de la barra cuando hay SOS. Siempre con sirena y la palabra «SOS».
- **Violeta reporte** (#6D28D9): reportes de incidencia (etiqueta, marcador circular, chip).
- **Verde azulado acompañamiento** (#0F766E; capa del mapa #0D9488): sesiones de acompañamiento, su recorrido y su marcador «A».
- **Escudo de sereno** (#1D3B78): marcador de sereno disponible; gris #8291A7 si no está disponible. Se distingue por forma, no por tono.

### Neutral
- **Fondo niebla** (#F3F6FB): fondo de página.
- **Superficie** (#FFFFFF) y **superficie hundida** (#EDF2F9): tarjetas y paneles; encabezados de tabla.
- **Tinta** (#0D1B33), **tinta secundaria** (#475A75), **tinta terciaria** (#5C6E88): texto principal, ayudas y metadatos.
- **Borde** (#D9E2EE), **borde fuerte** (#C3CFDF), **borde de control** (#7487A2, 3.66:1): separación de superficies, separadores y campos.

### Named Rules
**The Red Means SOS Rule.** El rojo solo marca un SOS o un caso urgente. Un error de formulario usa el mismo tono, pero siempre con icono de error y texto; nada decorativo es rojo.

**The No Color Alone Rule.** Todo estado lleva tono, icono y texto. Un chip sin palabra, o un marcador distinguible solo por color, es un defecto.

**The Muted City Rule.** Las teselas del mapa se apagan a azul grisáceo (`--mapa-filtro-teselas`) para que los casos resalten. Nunca se muestra el mapa a todo color.

## Typography

**Display Font:** Public Sans (con Segoe UI, system-ui)
**Body Font:** Public Sans
**Label Font:** Public Sans

**Character:** Una sola familia institucional, de cuerpo abierto y figuras tabulares: formal sin rigidez, legible a 13 px en paneles densos. Alojada en el proyecto (licencia OFL), sin CDN.

### Hierarchy
- **Display** (700, 36 px, 1.1, tnum): cifras protagonistas (cronómetro de la app, totales).
- **Headline** (700, 28 px, 1.2, -0.015em): título de página (`.titulo`), en marino.
- **Title** (600, 18 px): título de sección y de panel flotante.
- **Body** (400, 16 px, 1.5): texto corriente; medida de lectura 68ch.
- **Label** (600, 13 px): chips, etiquetas de campo, metadatos. 13 px es el mínimo.

### Named Rules
**The Tabular Figures Rule.** Toda cifra que se compara o cambia (horas, distancias, totales, contadores) usa `font-variant-numeric: tabular-nums`.

## Layout

El panel usa una barra superior fija de 60 px y, debajo, dos gramáticas. La central es mapa a sangre con paneles flotantes: cola de trabajo de 372 px a la izquierda, serenos de 300 px arriba a la derecha y leyenda en chips abajo, entre los dos; ambos paneles se pliegan. Las demás páginas usan un contenido centrado con encabezado de página (título y una línea de ayuda; la acción principal a la derecha). Los detalles de caso usan dos columnas desde 1024 px (principal 1.55fr, contexto ≥ 20rem). El espaciado sigue una escala de 4 px (4–64 px). Por debajo de 1100 px las secciones de la barra pasan a un menú; por debajo de 760 px los paneles flotantes se apilan bajo el mapa y las tablas se convierten en tarjetas, solo con CSS.

## Elevation & Depth

Sistema híbrido: superficies planas con borde de 1 px en reposo, y sombras teñidas de marino, siempre con desplazamiento y desenfoque, para lo que flota o pide atención.

### Shadow Vocabulary
- **Reposo** (`0 1px 2px rgb(11 31 68 / .06), 0 1px 1px rgb(11 31 68 / .04)`): tarjetas de lista, leyendas.
- **Atención** (`0 4px 12px -2px rgb(11 31 68 / .10), 0 2px 4px -2px rgb(11 31 68 / .06)`): tarjeta «Siguiente acción», hover de accesos.
- **Flotante** (`0 16px 40px -8px rgb(11 31 68 / .22), 0 6px 12px -6px rgb(11 31 68 / .12)`): paneles de la central y diálogos.

### Named Rules
**The No Halo Rule.** Ninguna sombra de desplazamiento cero ni halo de color; la profundidad viene del desplazamiento y del desenfoque.

## Shapes

Esquinas suaves y graduadas por jerarquía: 6 px en piezas pequeñas, 8 px en controles, 12 px en tarjetas, 16 px en paneles flotantes y diálogos, píldora en chips e indicadores. Los marcadores del mapa tienen forma por entidad: cuadro redondeado con sirena (SOS), círculo (reporte), escudo (sereno), círculo con «A» (acompañamiento).

## Components

### Buttons
- **Shape:** esquinas suaves (8 px), alto mínimo de 44 px (compacto: 32 px en escritorio denso).
- **Primary:** azul de acción con texto blanco, 8 px × 24 px.
- **Hover / Focus / Active:** hover #1A3FB0; foco con anillo azul de 2 px separado 2 px; al pulsar, escala 0.97 (100 ms).
- **Secondary:** superficie blanca, texto marino, borde de control; hover en azul tenue.
- **Danger:** rojo SOS, solo para crear un caso urgente o desactivar.

### Chips
- **Style:** píldora con fondo tenue, borde y texto del tono, icono de 14 px y el texto literal del estado (`_ChipEstado`).
- **Tonos:** información (recibido, en revisión), progreso (asignado, en camino, en atención), éxito (cerrado), advertencia (pendiente de un reporte), urgente (pendiente de un SOS), neutro (derivado, descartado, cancelado), acompañamiento.

### Cards / Containers
- **Corner Style:** 12 px; paneles flotantes 16 px.
- **Background:** superficie blanca sobre fondo niebla.
- **Shadow Strategy:** reposo en listas; flotante en la central (ver Elevation & Depth).
- **Border:** 1 px #D9E2EE; nunca una franja de color lateral.
- **Internal Padding:** 16–32 px.

### Inputs / Fields
- **Style:** borde de control de 1 px, fondo blanco, 8 px de radio, 44 px de alto.
- **Focus:** anillo azul de 2 px.
- **Error:** borde y mensaje en rojo con icono; resumen de errores arriba del formulario.

### Navigation
- **Barra superior:** marino noche, marca con escudo, secciones de 14 px semibold; la activa en marino elevado con subrayado celeste; persona con iniciales y «Salir». En la central suma indicadores en vivo (SOS en rojo cuando hay, por asignar, serenos libres, acompañamientos) que laten una vez al cambiar.

### Siguiente acción (componente propio)
La tarjeta principal de un detalle: borde azul de acción, sombra de atención, título en imperativo («Clasifica el SOS…», «Decide qué hacer con el reporte») y una etiqueta «Paso n de 3». Sobre ella, la línea de progreso con pasos hechos, actual, omitidos y pendientes; debajo, «Otras acciones» plegadas, que se abren solas si vuelven con errores.

### Vuelo al caso (interacción propia)
«Ver en el mapa» lleva el mapa al punto con zoom 18 en 0.9 s, centrado en la franja que no tapan los paneles; al llegar, dos anillos del color del caso se abren desde el punto y el marcador queda fijado con un anillo marino. «Elegir sereno» encuadra el caso con el sereno más cercano y su línea de despacho. Con movimiento reducido, el salto es inmediato y sin anillos.

## Do's and Don'ts

### Do:
- **Do** usar el azul de acción (#1D4ED8) para una sola acción principal por vista.
- **Do** acompañar cada estado con icono y texto literal (`_ChipEstado`).
- **Do** apagar las teselas con `--mapa-filtro-teselas` en todo mapa del panel.
- **Do** animar solo `transform` y `opacity`, con las curvas y duraciones de los tokens, y anular todo con `prefers-reduced-motion`.
- **Do** dar a cada contenedor desplazable `tabindex="0"`, `role="region"` y un nombre.

### Don't:
- **Don't** usar rótulos (kickers) sobre los títulos.
- **Don't** usar franjas de color de más de 1 px en el borde izquierdo o derecho de tarjetas, listas o avisos.
- **Don't** usar el rojo fuera de un SOS, un caso urgente o un error con icono.
- **Don't** montar inicios con tarjetas de cifras sueltas más tarjetas de icono y texto; el resumen va en una línea o en una franja dividida.
- **Don't** añadir animaciones persistentes: la única es el pulso del SOS sin abrir.
