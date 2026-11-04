using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.CargadorDataset;

// Deja la base de datos como recién desplegada: sin casos, acompañamientos ni cuentas, salvo el administrador y el usuario del
// sistema (actor de las acciones automáticas, S6-T05). Conserva lo que el sistema necesita para arrancar (historial de migraciones,
// roles, categorías y claves de las cookies del panel). La admisión automática vuelve a «Apagada», como la siembra la migración.
// Corre dentro de la transacción de la carga: si algo falla después, la base de datos queda como estaba.
public static class Vaciado
{
    private static readonly string[] Conservadas =
        ["__EFMigrationsHistory", "AspNetRoles", "AspNetRoleClaims", "CategoriasIncidencia", "DataProtectionKeys", "AspNetUsers", "AspNetUserRoles"];

    public static async Task VaciarAsync(CadDbContext db)
    {
        var lista = string.Join(", ", Conservadas.Select(t => $"'{t}'"));
        await db.Database.ExecuteSqlRawAsync($$"""
            DO $$
            DECLARE r record;
            BEGIN
              FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename NOT IN ({{lista}})
              LOOP
                EXECUTE format('TRUNCATE TABLE public.%I RESTART IDENTITY CASCADE', r.tablename);
              END LOOP;
            END $$;
            """);

        // Las cuentas: solo quedan el administrador y el usuario del sistema (que no tiene rol).
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM "AspNetUserRoles" WHERE "UserId" NOT IN (
              SELECT ur."UserId" FROM "AspNetUserRoles" ur JOIN "AspNetRoles" r ON r."Id" = ur."RoleId" WHERE r."Name" = 'Administrador');
            """);
        await db.Database.ExecuteSqlAsync($"""
            DELETE FROM "AspNetUsers" WHERE "Id" NOT IN (SELECT "UserId" FROM "AspNetUserRoles")
              AND "Id" <> {Cad.Server.Modulos.Cuentas.Dominio.UsuarioSistema.Id};
            """);

        db.AjustesIa.Add(Cad.Server.Modulos.Asistencia.Dominio.AjusteIa.PorDefecto());
        await db.SaveChangesAsync();
    }
}
