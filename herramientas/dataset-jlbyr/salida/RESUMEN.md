# Resumen del dataset (generado; semilla 20261027)

Periodo simulado: 31/08/2026 – 30/11/2026 (91 días). Todas las fechas se desplazan al cargar.

- Casos: **1047** · ciudadanos: 430 · serenos: 36 · operadores: 6 · acompañamientos: 120
- Casos por día: 11.5 en promedio

## Por origen

| Origen | Casos | % |
|---|---:|---:|
| Reporte | 732 | 69.9 % |
| Sos | 307 | 29.3 % |
| Acompanamiento | 8 | 0.8 % |

## Por categoría (la final, tras clasificar o corregir)

| Categoría | SOS | Reportes | Total |
|---|---:|---:|---:|
| Robo o hurto | 115 | 161 | 276 |
| Persona o vehículo sospechoso | 13 | 96 | 109 |
| Consumo de alcohol en la vía pública | 0 | 93 | 93 |
| Ruidos molestos o escándalo | 0 | 84 | 84 |
| Violencia familiar | 52 | 25 | 77 |
| Peleas o riñas | 33 | 38 | 71 |
| Consumo o venta de drogas | 11 | 55 | 66 |
| Accidente de tránsito | 15 | 47 | 62 |
| Acoso en la vía pública | 34 | 28 | 62 |
| Persona en riesgo | 24 | 24 | 48 |
| Daños o vandalismo | 0 | 36 | 36 |
| Extorsión o amenazas | 12 | 23 | 35 |
| Otros | 6 | 22 | 28 |

## Estado al final del periodo

| Estado | Casos |
|---|---:|
| Cerrada | 755 |
| Descartada | 117 |
| Derivada | 88 |
| Cancelada | 32 |
| Pendiente | 29 |
| Recibido | 14 |
| EnRevision | 6 |
| EnAtencion | 3 |
| EnCamino | 2 |
| Asignada | 1 |

## Abiertos al cargar

| Origen | Estado | Casos | Desde hace |
|---|---|---:|---|
| Reporte | EnAtencion | 1 | 4–4 min |
| Reporte | EnCamino | 1 | 3–3 min |
| Reporte | EnRevision | 6 | 1–5 min |
| Reporte | Pendiente | 18 | 3–30 min |
| Reporte | Recibido | 14 | 2–58 min |
| Sos | Asignada | 1 | 1–1 min |
| Sos | EnAtencion | 2 | 19–24 min |
| Sos | EnCamino | 1 | 72–72 min |
| Sos | Pendiente | 11 | 1–8 min |

Serenos de turno sin caso al cargar: 3 (para asignar en vivo).

## Tiempos de respuesta (registro → inicio de la atención)

- SOS y urgentes: mediana 12.9 min · p90 26.7 min (262 casos)
- Reportes: mediana 35.2 min · p90 72.9 min (493 casos)

## Evaluaciones

- 239 evaluaciones (41.5 % de los casos cerrados con ciudadano) · promedio 3.93 de 5
- Distribución: 1★ 7 · 2★ 11 · 3★ 50 · 4★ 94 · 5★ 77

## Hora del día (todos los casos)

```
00 h █████████████████ 51
01 h ███████████████████ 57
02 h ██████████████ 42
03 h ██████████ 30
04 h ██████ 19
05 h █████ 15
06 h ██████████ 31
07 h ███████ 20
08 h █████████ 28
09 h ██████ 19
10 h ██████████ 30
11 h ████████ 24
12 h ███████ 22
13 h ████████████ 38
14 h █████████ 28
15 h ██████████████ 42
16 h ███████████ 34
17 h ██████████ 30
18 h ████████████████ 50
19 h ████████████████████████████████████████ 122
20 h █████████████████████████████ 89
21 h ██████████████████████ 67
22 h ███████████████████████ 70
23 h █████████████████████████████ 89
```

## Día de la semana

| Día | Casos |
|---|---:|
| lunes | 178 |
| martes | 109 |
| miércoles | 99 |
| jueves | 137 |
| viernes | 178 |
| sábado | 189 |
| domingo | 157 |

## Dónde (tipo de lugar de OpenStreetMap)

| Lugar | Casos |
|---|---:|
| residencial | 288 |
| avenida | 180 |
| parque | 173 |
| paradero | 96 |
| mercado | 96 |
| noche | 94 |
| comercio | 70 |
| colegio | 50 |

## Sector de patrullaje

| Sector | Casos |
|---|---:|
| Sector 1 | 334 |
| Sector 2 | 309 |
| Sector 3 | 189 |
| Sector 4 | 215 |

Sin ubicación en el mapa: 115 casos (SOS sin GPS y reportes con solo una referencia en texto).
