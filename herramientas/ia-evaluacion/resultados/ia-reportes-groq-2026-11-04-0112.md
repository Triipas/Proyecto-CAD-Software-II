# Evaluación de la asistencia con IA: reportes (04/11/2026 01:12)

> **Datos simulados.** Los reportes y las decisiones son del dataset de demostración JLByR (generado). Las cifras sirven para
> comparar variantes entre sí y ver que el flujo funciona, **no** como promesa de rendimiento real (plan, 14).

- Proveedor: **Groq** (openai/gpt-oss-20b → openai/gpt-oss-120b). Prompt `2026-11-c`, criterios `2026-11-borrador`, 6 ejemplos por reporte.
- Muestra: 30 reportes decididos por operadores, repartidos entre admitidos, derivados y descartados (semilla 20261104).
- Verdad: la categoría final del caso y la decisión que registró el operador. Los ejemplos del prompt excluyen al propio caso.

## Resultados

| Medida | Modelo | Reglas (línea base) | Categoría del ciudadano |
|---|---|---|---|
| Respondió | 100 % (30/30) | 100 % | — |
| Categoría correcta | 93.3 % (28/30) | 90 % (27/30) | 93.3 % (28/30) |
| Decisiones tomadas (no «revisar con cuidado») | 100 % (30/30) | 56.7 % (17/30) | — |
| Decisión correcta, entre las tomadas | 33.3 % (10/30) | 41.2 % (7/17) | — |

### Decisión por tipo real (modelo)

| Real \ Sugerida | Admitir | Derivar | Descartar | Revisar con cuidado | Sin respuesta |
|---|---|---|---|---|---|
| Admitir | 10 | 0 | 0 | 0 | 0 |
| Derivar | 10 | 0 | 0 | 0 | 0 |
| Descartar | 10 | 0 | 0 | 0 | 0 |

### Calibración: aciertos de decisión por nivel de confianza (modelo)

| Confianza | Reportes | Decisión correcta |
|---|---|---|
| Alta | 30 | 33.3 % (10/30) |
| Media | 0 | — |
| Baja | 0 | — |

### Costo y tiempo

- Pasaron al modelo preciso: 10 % (3/30). Marcadas con posible urgencia: 3.
- Tiempo por reporte: mediana 1.0 s, p90 2.0 s.
- Tokens: 106,586 en total, 3,552 por reporte.
- Errores: ninguno.

## Límites

- Muestra pequeña por la cuota del nivel gratuito: los porcentajes tienen mucho margen de error.
- La «verdad» la puso el generador del dataset, no un etiquetado independiente de tres personas (plan, 9.2): falta el techo humano.
- Los ejemplos pueden incluir decisiones posteriores al reporte evaluado (en el uso real, solo hay anteriores).
