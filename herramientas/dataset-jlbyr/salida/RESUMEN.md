# Resumen del dataset (generado; semilla 20261027)

Periodo simulado: 31/08/2026 – 30/11/2026 (91 días). Todas las fechas se desplazan al cargar.

- Casos: **5017** · ciudadanos: 1300 · serenos: 36 · operadores: 6 · acompañamientos: 360
- Casos por día: 55.1 en promedio

## Por origen

| Origen | Casos | % |
|---|---:|---:|
| Reporte | 3408 | 67.9 % |
| Sos | 1585 | 31.6 % |
| Acompanamiento | 24 | 0.5 % |

## Por categoría (la final, tras clasificar o corregir)

| Categoría | SOS | Reportes | Total |
|---|---:|---:|---:|
| Robo o hurto | 582 | 684 | 1266 |
| Consumo de alcohol en la vía pública | 0 | 487 | 487 |
| Persona o vehículo sospechoso | 64 | 357 | 421 |
| Violencia familiar | 273 | 147 | 420 |
| Ruidos molestos o escándalo | 0 | 398 | 398 |
| Peleas o riñas | 182 | 214 | 396 |
| Accidente de tránsito | 82 | 232 | 314 |
| Consumo o venta de drogas | 24 | 280 | 304 |
| Acoso en la vía pública | 159 | 129 | 288 |
| Persona en riesgo | 115 | 110 | 225 |
| Extorsión o amenazas | 89 | 98 | 187 |
| Daños o vandalismo | 0 | 177 | 177 |
| Otros | 39 | 95 | 134 |

## Estado al final del periodo

| Estado | Casos |
|---|---:|
| Cerrada | 3898 |
| Descartada | 526 |
| Derivada | 409 |
| Cancelada | 159 |
| Pendiente | 10 |
| Recibido | 6 |
| EnRevision | 3 |
| EnAtencion | 3 |
| EnCamino | 2 |
| Asignada | 1 |

## Abiertos al cargar

| Origen | Estado | Casos | Desde hace |
|---|---|---:|---|
| Reporte | Asignada | 1 | 1–1 min |
| Reporte | EnAtencion | 2 | 17–18 min |
| Reporte | EnCamino | 1 | 5–5 min |
| Reporte | EnRevision | 3 | 3–5 min |
| Reporte | Pendiente | 7 | 8–28 min |
| Reporte | Recibido | 6 | 3–46 min |
| Sos | EnAtencion | 1 | 6–6 min |
| Sos | EnCamino | 1 | 4–4 min |
| Sos | Pendiente | 3 | 1–3 min |

Serenos de turno sin caso al cargar: 4 (para asignar en vivo).

## Tiempos de respuesta (registro → inicio de la atención)

- SOS y urgentes: mediana 13.1 min · p90 26.9 min (1421 casos)
- Reportes: mediana 35.7 min · p90 73.4 min (2477 casos)

## Evaluaciones

- 1232 evaluaciones (42.4 % de los casos cerrados con ciudadano) · promedio 3.96 de 5
- Distribución: 1★ 37 · 2★ 61 · 3★ 235 · 4★ 476 · 5★ 423

## Hora del día (todos los casos)

```
00 h ██████████████████████████████████ 327
01 h ███████████████████████████████ 297
02 h █████████████████████████ 240
03 h █████████████████████ 199
04 h ██████████████ 133
05 h ████████████ 117
06 h ███████████ 101
07 h █████████████ 126
08 h ███████████████ 143
09 h █████████████ 124
10 h ██████████████████ 170
11 h ███████████████████ 178
12 h ████████████████ 149
13 h █████████████ 124
14 h ███████████████ 141
15 h ███████████████ 141
16 h ████████████████ 151
17 h █████████████████████ 197
18 h ███████████████████████ 223
19 h ██████████████████████████████ 286
20 h █████████████████████████████████████ 350
21 h ██████████████████████████████████████ 366
22 h ████████████████████████████████████████ 383
23 h █████████████████████████████████████ 351
```

## Día de la semana

| Día | Casos |
|---|---:|
| lunes | 582 |
| martes | 553 |
| miércoles | 644 |
| jueves | 598 |
| viernes | 887 |
| sábado | 1006 |
| domingo | 747 |

## Dónde (tipo de lugar de OpenStreetMap)

| Lugar | Casos |
|---|---:|
| residencial | 1352 |
| parque | 819 |
| avenida | 796 |
| mercado | 594 |
| noche | 530 |
| paradero | 399 |
| comercio | 346 |
| colegio | 181 |

## Sector de patrullaje

| Sector | Casos |
|---|---:|
| Sector 1 | 1517 |
| Sector 2 | 1604 |
| Sector 3 | 806 |
| Sector 4 | 1090 |

Sin ubicación en el mapa: 665 casos (SOS sin GPS y reportes con solo una referencia en texto).
