using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Persistencia;

public class CadDbContext(DbContextOptions<CadDbContext> options)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Usuario>(usuario =>
        {
            usuario.Property(u => u.Nombres).HasMaxLength(100);
            usuario.Property(u => u.Apellidos).HasMaxLength(100);
            usuario.Property(u => u.PhoneNumber).HasMaxLength(20);

            // Identity crea EmailIndex sin unicidad; el correo es el identificador de acceso,
            // así que la base de datos debe impedir duplicados aun con registros simultáneos (HU01-CA05).
            usuario.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        });

        // Calificado: dentro de IdentityDbContext, «Roles» es el DbSet de roles.
        builder.Entity<IdentityRole<Guid>>().HasData(Modulos.Cuentas.Dominio.Roles.Todos.Select(r => new IdentityRole<Guid>
        {
            Id = r.Id,
            Name = r.Nombre,
            NormalizedName = r.Nombre.ToUpperInvariant(),
            ConcurrencyStamp = r.Id.ToString(),
        }));
    }
}
