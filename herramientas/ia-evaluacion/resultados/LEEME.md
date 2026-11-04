# Resultados de la herramienta ia-evaluacion

Informes que genera `dotnet run --project herramientas/ia-evaluacion -- <comando>`. Todos usan **datos simulados**.

## `ia-reportes-groq-2026-11-04-0112` (S6-T14, 04/11/2026)

Groq (`gpt-oss-20b`, con escalada a `gpt-oss-120b`) sobre 30 reportes del dataset JLByR decididos por operadores: 10 admitidos,
10 derivados y 10 descartados. Prompt `2026-11-c`, criterios `2026-11-borrador` y 6 ejemplos.

Lectura:

- **La categoría está bien** (93 %), pero no mejora a la del ciudadano (también 93 %): en el dataset el ciudadano casi siempre acierta. Para
  medir HU46 hacen falta reportes con la categoría del ciudadano equivocada, como prevé el corpus de S6-T03 (≈ 20 % de errores sembrados).
- **La decisión no se puede medir con este dataset.** El modelo admitió los 30. Al revisar los derivados y descartados, sus decisiones no
  se deducen del texto: un robo de celular «derivado a la municipalidad», unas lunas rotas «descartadas por fuera de alcance» y muchos
  descartes por «duplicado» o porque «el ciudadano no responde», que dependen de datos que no están en la descripción. Según los criterios
  de triaje, admitir esos reportes es defendible. La verdad para HU47 tiene que venir del corpus por escenarios (S6-T03) y del
  subconjunto etiquetado por tres personas (plan, 9.2).
- **La confianza no está calibrada**: 30 respuestas con confianza «Alta» y 33 % de acierto contra esta verdad. Es el riesgo que prevé el
  plan (5.2 y 7.7) y la razón por la que la admisión automática exige la segunda opinión y pasar antes por «Sombra», con al menos 100
  casos y un 98 % de precisión antes de activarla.
- **Costo y tiempo**: 3552 tokens por reporte (con criterios y ejemplos), 1 s de mediana y 2 s de p90, y 10 % escaladas. Con la cuota
  gratuita de Groq (unos 8000 tokens por minuto) se analizan dos reportes por minuto.
