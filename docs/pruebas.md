# Pruebas no funcionales

## RNF02 / HU11-CA05 — presentación de SOS en la central (S1-T18)

**Requisito (Plan Scrum):** con 20 sesiones concurrentes en el entorno de pruebas, al menos el 95 % de los SOS deberá mostrarse y notificarse en central dentro de 3 segundos desde su aceptación por el servidor. Una prueba de al menos 100 SOS registrará aceptación y presentación. **No es el tiempo de respuesta del sereno.**

### Qué se mide

`PresentadoEn − AceptadoEn`, ambos con el **reloj del servidor** (sin desfase entre relojes):

- `AceptadoEn`: momento en que el servidor registra el SOS (`Caso.RegistradoEn`).
- `PresentadoEn`: llegada del acuse que envía un **panel real** de operador cuando el SOS ya se muestra en la sección urgente y se ha emitido el aviso visual y sonoro.
- Un SOS sin acuse cuenta como incumplimiento.

La instrumentación está **apagada por defecto** (`Medicion:Rnf02:Habilitada=false`): apagada no registra nada y el endpoint de resultados responde 404.

### Cómo ejecutarla

1. **Activar la medición en el servidor** (variable de entorno): `Medicion__Rnf02__Habilitada=true`. En el entorno publicado, «calentar» antes el servicio (ADR-06).
2. **Abrir un panel real** con una cuenta de operador en `/panel/central` y esperar «Conectado en tiempo real». Dejarlo abierto y visible durante toda la prueba.
3. **Lanzar la carga** (credenciales del administrador solo en variables de entorno):
   ```bash
   dotnet run --project tests/Cad.PruebaCarga -- --url https://SERVIDOR --entorno validacion-render --ciudadanos 19 --paneles 1 --sos 100
   ```
   `--ciudadanos 19` + `--paneles 1` = 20 sesiones concurrentes. Variables: `CAD_PRUEBA_ADMIN_CORREO`, `CAD_PRUEBA_ADMIN_CLAVE`.
4. **Desactivar la medición** al terminar.

La herramienta crea cuentas de ciudadano ficticias (`carga.<ejecución>.<n>@correo.test`), emite los SOS de forma concurrente (la mitad sin ubicación, coordenadas aleatorias en Lima), espera los acuses y escribe el informe en `docs/mediciones/` (Markdown y JSON). Código de salida: 0 si cumple, 1 si no.

**Efectos en el entorno:** los SOS generados quedan como pendientes en la base de datos (la cancelación es HU09, Sprint 5). Son datos ficticios.

### Resultados registrados

| Fecha | Entorno | Resultado | Informe |
|---|---|---|---|
| 13/10/2026 | Ensayo local (no válido para RNF02) | 100/100 en ≤ 3 s; p95 178 ms | [rnf02-20261013T074839-ensayo-local.md](mediciones/rnf02-20261013T074839-ensayo-local.md) |
| 14/10/2026 | Ensayo local con el código del Sprint 2 (no válido para RNF02) | 100/100 en ≤ 3 s; p95 63 ms | [rnf02-20261014T223258-ensayo-local-sprint2.md](mediciones/rnf02-20261014T223258-ensayo-local-sprint2.md) |
| — | Entorno de validación publicado | Planificada en el Sprint 3 (S1-T18 y S2-T15) | — |
