# Resumen del dataset (generado; semilla 20261027)

Periodo simulado: 31/08/2026 – 30/11/2026 (91 días). Todas las fechas se desplazan al cargar.

- Casos: **4049** · ciudadanos: 1300 · serenos: 36 · operadores: 6 · acompañamientos: 360
- Casos por día: 44.5 en promedio

## Por origen

| Origen | Casos | % |
|---|---:|---:|
| Reporte | 2760 | 68.2 % |
| Sos | 1265 | 31.2 % |
| Acompanamiento | 24 | 0.6 % |

## Por categoría (la final, tras clasificar o corregir)

| Categoría | SOS | Reportes | Total |
|---|---:|---:|---:|
| Robo o hurto | 468 | 561 | 1029 |
| Consumo de alcohol en la vía pública | 0 | 403 | 403 |
| Persona o vehículo sospechoso | 48 | 286 | 334 |
| Violencia familiar | 216 | 114 | 330 |
| Peleas o riñas | 146 | 165 | 311 |
| Ruidos molestos o escándalo | 0 | 308 | 308 |
| Accidente de tránsito | 66 | 194 | 260 |
| Consumo o venta de drogas | 22 | 236 | 258 |
| Acoso en la vía pública | 125 | 107 | 232 |
| Persona en riesgo | 95 | 88 | 183 |
| Extorsión o amenazas | 71 | 81 | 152 |
| Daños o vandalismo | 0 | 150 | 150 |
| Otros | 32 | 67 | 99 |

## Estado al final del periodo

| Estado | Casos |
|---|---:|
| Cerrada | 3125 |
| Descartada | 420 |
| Derivada | 327 |
| Cancelada | 122 |
| Pendiente | 31 |
| Recibido | 12 |
| EnRevision | 6 |
| EnAtencion | 4 |
| EnCamino | 2 |

## Abiertos al cargar

| Origen | Estado | Casos | Desde hace |
|---|---|---:|---|
| Reporte | EnAtencion | 2 | 12–12 min |
| Reporte | EnCamino | 1 | 0–0 min |
| Reporte | EnRevision | 6 | 3–6 min |
| Reporte | Pendiente | 19 | 5–29 min |
| Reporte | Recibido | 12 | 14–53 min |
| Sos | EnAtencion | 2 | 4–43 min |
| Sos | EnCamino | 1 | 21–21 min |
| Sos | Pendiente | 12 | 1–8 min |

Serenos de turno sin caso al cargar: 4 (para asignar en vivo).

## Tiempos de respuesta (registro → inicio de la atención)

- SOS y urgentes: mediana 13.1 min · p90 27.2 min (1131 casos)
- Reportes: mediana 35.7 min · p90 75.2 min (1994 casos)

## Evaluaciones

- 957 evaluaciones (41.2 % de los casos cerrados con ciudadano) · promedio 3.86 de 5
- Distribución: 1★ 46 · 2★ 57 · 3★ 188 · 4★ 357 · 5★ 309

## Hora del día (todos los casos)

```
00 h █████████████████████████████████ 256
01 h ██████████████████████████████ 236
02 h █████████████████████████ 192
03 h ████████████████████ 156
04 h ██████████████ 111
05 h ████████████ 90
06 h ███████████ 83
07 h █████████████ 99
08 h ███████████████ 117
09 h ██████████████ 106
10 h █████████████████ 129
11 h ██████████████████ 140
12 h ███████████████ 119
13 h █████████████ 103
14 h ██████████████ 111
15 h ███████████████ 114
16 h ████████████████ 121
17 h ████████████████████ 156
18 h ███████████████████████ 176
19 h ██████████████████████████████████ 267
20 h ████████████████████████████████████ 283
21 h ██████████████████████████████████████ 295
22 h ████████████████████████████████████████ 311
23 h ████████████████████████████████████ 278
```

## Día de la semana

| Día | Casos |
|---|---:|
| lunes | 491 |
| martes | 433 |
| miércoles | 517 |
| jueves | 480 |
| viernes | 729 |
| sábado | 799 |
| domingo | 600 |

## Dónde (tipo de lugar de OpenStreetMap)

| Lugar | Casos |
|---|---:|
| residencial | 1057 |
| parque | 673 |
| avenida | 646 |
| mercado | 468 |
| noche | 433 |
| paradero | 329 |
| comercio | 290 |
| colegio | 153 |

## Sector de patrullaje

| Sector | Casos |
|---|---:|
| Sector 1 | 1190 |
| Sector 2 | 1301 |
| Sector 3 | 669 |
| Sector 4 | 889 |

Sin ubicación en el mapa: 551 casos (SOS sin GPS y reportes con solo una referencia en texto).
