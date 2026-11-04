# Backtest de la proyección de focos: mundo B (04/11/2026)

> **Datos simulados** con el generador del proyecto (Cad.DatosDemo). Las cifras comparan métodos entre sí y frente al oráculo;
> no predicen delitos reales (plan, 14).

- 13495 casos en 78 semanas y 90 celdas de 0,005°. Se predicen las últimas 12 semanas,
  bloque a bloque (4 h), entrenando con todo lo anterior y reentrenando cada 4 semanas.
- Acierto: parte de los casos que cayó en el 10 % de celdas con más casos esperados. PAI = acierto ÷ 10 % (1 es azar).
- Fracción del oráculo: PAI del método ÷ PAI de la intensidad verdadera del generador.

| Método | Acierto (10 % del área) | PAI | Fracción del oráculo | Desviación de Poisson | Error absoluto |
|---|---|---|---|---|---|
| Árboles (FastTree Tweedie) | 56.4 % | 5.64 | 99.2 % | 0.2265 | 0.0744 |
| Conjunto 0.50 | 56.1 % | 5.61 | 98.8 % | 0.2093 | 0.0774 |
| Conjunto 0.75 | 55.9 % | 5.59 | 98.5 % | 0.2091 | 0.0759 |
| Conjunto 0.25 | 55.5 % | 5.55 | 97.7 % | 0.2109 | 0.0790 |
| Línea base | 54.4 % | 5.44 | 95.9 % | 0.2136 | 0.0805 |
| Más casos en total | 49.2 % | 4.92 | 86.7 % | 274.6062 | 137.5587 |
| Promedio sin memoria | 49.2 % | 4.92 | 86.7 % | 0.2245 | 0.0796 |
| La semana pasada se repite | 17.9 % | 1.79 | 31.6 % | 1.0674 | 0.0784 |
| Azar | 11.0 % | 1.10 | 19.4 % | 1.0090 | 0.5020 |
| Oráculo | 56.8 % | 5.68 | 100.0 % | 0.2076 | 0.0692 |

Se despliega **Conjunto 0.75** (peso del modelo en el conjunto: 0.75), el de menor desviación de Poisson.

Foco emergente (CA06), puesto de su celda cada semana (1 = la más probable):

```json
{"foco":"Foco emergente (colegio San Vicente de Paúl)","celda":"-3288:-14305","puestos":[{"semana":1,"lineaBase":9,"sinMemoria":22},{"semana":2,"lineaBase":9,"sinMemoria":22},{"semana":3,"lineaBase":9,"sinMemoria":22},{"semana":4,"lineaBase":8,"sinMemoria":22},{"semana":5,"lineaBase":8,"sinMemoria":21},{"semana":6,"lineaBase":7,"sinMemoria":21},{"semana":7,"lineaBase":7,"sinMemoria":19},{"semana":8,"lineaBase":8,"sinMemoria":19},{"semana":9,"lineaBase":7,"sinMemoria":19},{"semana":10,"lineaBase":6,"sinMemoria":17},{"semana":11,"lineaBase":6,"sinMemoria":17},{"semana":12,"lineaBase":7,"sinMemoria":17}]}
```
