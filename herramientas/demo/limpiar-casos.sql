-- Deja la central sin casos (05/11/2026), para volver a sembrar la demostración con sembrar.mjs.
-- Borra TODOS los SOS y reportes con lo que cuelga de ellos (eventos, asignaciones, revisiones, decisiones, cierres, fotos,
-- notificaciones, sugerencias de la IA), el acompañamiento (sus avisos y confirmaciones se vinculan a SOS) y las mediciones
-- de presentación de esos casos. Deja a los serenos fuera de servicio (sin posiciones vigentes en el mapa).
-- Conserva las cuentas (personal y ciudadanos), las categorías, los ajustes de la IA y la analítica agregada.
-- Irreversible: se ejecuta en una transacción con limpiar.mjs (que pide confirmación) o en el editor SQL de Neon.
BEGIN;

DELETE FROM "ActuacionesAviso";
DELETE FROM "AvisosAcompanamiento";
DELETE FROM "ConfirmacionesBienestar";
DELETE FROM "EstadosInformados";
DELETE FROM "ConservacionesRecorrido";
DELETE FROM "PosicionesAcompanamiento";
DELETE FROM "SesionesAcompanamiento";

DELETE FROM "MedicionesPresentacion" WHERE "CasoId" IS NOT NULL;
DELETE FROM "EvaluacionesAdmision";
DELETE FROM "SugerenciasReporte";
DELETE FROM "Reasignaciones";
DELETE FROM "IniciosAtencion";
DELETE FROM "EvaluacionesCaso";
DELETE FROM "CierresCaso";
DELETE FROM "Asignaciones";
DELETE FROM "CancelacionesSos";
DELETE FROM "CorreccionesCategoria";
DELETE FROM "DecisionesReporte";
DELETE FROM "DescartesCaso";
DELETE FROM "FotosReporte";
DELETE FROM "DetallesReporte";
DELETE FROM "IntentosContacto";
DELETE FROM "NotificacionesCiudadano";
DELETE FROM "EventosCaso";
DELETE FROM "Casos";

UPDATE "EstadosSereno" SET "Operativo" = FALSE, "CambiadoEn" = now() WHERE "Operativo";

COMMIT;
