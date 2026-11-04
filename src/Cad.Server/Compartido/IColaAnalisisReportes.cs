namespace Cad.Server.Compartido;

// HU44 y HU45 (S6-T11): después de registrar un reporte se pide su análisis en segundo plano. Encolar no espera a la IA ni
// puede fallar el registro: si no hay asistente, el pedido se ignora (principio 3 del plan: sin IA, todo sigue igual).
public interface IColaAnalisisReportes
{
    void Encolar(Guid casoId);
}
