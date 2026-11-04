# Backtest de la proyección de focos: mundo A (04/11/2026)

> **Datos simulados** con el generador del proyecto (Cad.DatosDemo). Las cifras comparan métodos entre sí y frente al oráculo;
> no predicen delitos reales (plan, 14).

- 14057 casos en 78 semanas y 90 celdas de 0,005°. Se predicen las últimas 12 semanas,
  bloque a bloque (4 h), entrenando con todo lo anterior y reentrenando cada 4 semanas.
- Acierto: parte de los casos que cayó en el 10 % de celdas con más casos esperados. PAI = acierto ÷ 10 % (1 es azar).
- Fracción del oráculo: PAI del método ÷ PAI de la intensidad verdadera del generador.

| Método | Acierto (10 % del área) | PAI | Fracción del oráculo | Desviación de Poisson | Error absoluto |
|---|---|---|---|---|---|
| Árboles (FastTree Tweedie) | 59.4 % | 5.94 | 99.3 % | 0.2327 | 0.0769 |
| Conjunto 0.75 | 58.9 % | 5.89 | 98.5 % | 0.2121 | 0.0786 |
| Conjunto 0.50 | 58.8 % | 5.88 | 98.4 % | 0.2125 | 0.0804 |
| Conjunto 0.25 | 58.8 % | 5.88 | 98.3 % | 0.2146 | 0.0822 |
| Línea base | 58.7 % | 5.87 | 98.1 % | 0.2180 | 0.0840 |
| Más casos en total | 55.8 % | 5.58 | 93.4 % | 284.9709 | 142.7627 |
| Promedio sin memoria | 55.8 % | 5.58 | 93.4 % | 0.2326 | 0.0828 |
| La semana pasada se repite | 23.7 % | 2.37 | 39.7 % | 1.0701 | 0.0815 |
| Azar | 10.2 % | 1.02 | 17.0 % | 1.0154 | 0.5044 |
| Oráculo | 59.8 % | 5.98 | 100.0 % | 0.2105 | 0.0732 |

Se despliega **Conjunto 0.75** (peso del modelo en el conjunto: 0.75), el de menor desviación de Poisson.

Foco emergente (CA06), puesto de su celda cada semana (1 = la más probable):

```json
{"foco":"Foco emergente (centro ferial El Polo)","celda":"-3286:-14308","puestos":[{"semana":1,"lineaBase":5,"sinMemoria":5},{"semana":2,"lineaBase":5,"sinMemoria":5},{"semana":3,"lineaBase":2,"sinMemoria":5},{"semana":4,"lineaBase":2,"sinMemoria":5},{"semana":5,"lineaBase":2,"sinMemoria":5},{"semana":6,"lineaBase":2,"sinMemoria":5},{"semana":7,"lineaBase":2,"sinMemoria":4},{"semana":8,"lineaBase":2,"sinMemoria":4},{"semana":9,"lineaBase":2,"sinMemoria":4},{"semana":10,"lineaBase":2,"sinMemoria":4},{"semana":11,"lineaBase":2,"sinMemoria":4},{"semana":12,"lineaBase":2,"sinMemoria":4}]}
```
