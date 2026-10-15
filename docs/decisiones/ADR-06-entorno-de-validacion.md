# ADR-06 — Entorno de validación publicado (decisión E7)

- **Fecha:** 13/10/2026
- **Estado:** decidida por delegación del equipo («tú decide el hosting»).
- **Relación:** concreta el ADR-05 del Plan de Arquitectura (Render + Neon).

## Contexto

- TEC01 (S1-T01, S1-T02, S1-T08) y la aceptación de RNF02 y RNF09 exigen un entorno de validación publicado con HTTPS/WSS.
- Presupuesto mínimo; regla del equipo: **ninguna operación con costo sin autorización expresa**.
- Hechos del Plan de Arquitectura (4.1, consultados el 04/10/2026): Render Free suspende el servicio tras 15 min sin tráfico (arranque de ~1 min) y puede reiniciarlo; la PostgreSQL gratuita de Render caduca a los 30 días; Neon Free escala a cero tras 5 min.

## Decisión

**Render Web Service (Docker) en plan gratuito + Neon PostgreSQL gratuito**, en la misma región (EE. UU. Este). **Costo: USD 0.**

- No se contrata Render Starter (≈ USD 7/mes prorrateado). Solo sería necesario para la prueba de disponibilidad de 8 h (RNF10), que no es del Sprint 1; se pedirá autorización expresa cuando corresponda.
- Las cuentas de Render y Neon, y la conexión de GitHub con Render (OAuth), las crea y gestiona el equipo.

## Consecuencias

- **Medición de RNF02 válida solo con el servicio activo:** antes de medir se «calienta» el servicio (abrir el panel y esperar «Conectado en tiempo real»). Durante la medición el tráfico (WebSocket y SOS) impide la suspensión. El informe registra el entorno y la fecha; si hubo una suspensión o un reinicio durante la prueba, la medición se repite.
- **Primer acceso tras la inactividad lento** (arranque de Render y de Neon): aceptable para demostraciones si se calienta antes; no se presenta como servicio real.
- **Revisión:** si las suspensiones o la cuota de Neon impiden medir, o para RNF10, se evaluará Render Starter (con autorización) o Render Postgres de pago.
