using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cad.Server.Persistencia;

// Solo para `dotnet ef migrations add`: generar una migración no abre la conexión,
// así que no hace falta ninguna credencial. En ejecución se usa ConnectionStrings:Cad.
public class CadDbContextFactory : IDesignTimeDbContextFactory<CadDbContext>
{
    public CadDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql("Host=localhost;Database=cad").Options);
}
