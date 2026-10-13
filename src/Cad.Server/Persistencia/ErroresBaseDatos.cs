using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cad.Server.Persistencia;

public static class ErroresBaseDatos
{
    // Un índice único rechazó la escritura: es la garantía de unicidad bajo concurrencia.
    public static bool EsViolacionDeUnicidad(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
