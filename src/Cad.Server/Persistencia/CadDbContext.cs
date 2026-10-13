using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Persistencia;

public class CadDbContext(DbContextOptions<CadDbContext> options)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Caso> Casos => Set<Caso>();
    public DbSet<EventoCaso> EventosCaso => Set<EventoCaso>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Caso>(caso =>
        {
            caso.ToTable("Casos", tabla =>
            {
                // La base de datos garantiza las reglas de ubicación (HU08) aunque falle el código.
                tabla.HasCheckConstraint("CK_Casos_UbicacionCompleta",
                    "(\"Latitud\" IS NULL AND \"Longitud\" IS NULL AND \"PrecisionMetros\" IS NULL AND \"UbicacionObtenidaEn\" IS NULL) OR " +
                    "(\"Latitud\" IS NOT NULL AND \"Longitud\" IS NOT NULL AND \"PrecisionMetros\" IS NOT NULL AND \"UbicacionObtenidaEn\" IS NOT NULL)");
                tabla.HasCheckConstraint("CK_Casos_Latitud", "\"Latitud\" BETWEEN -90 AND 90");
                tabla.HasCheckConstraint("CK_Casos_Longitud", "\"Longitud\" BETWEEN -180 AND 180");
                tabla.HasCheckConstraint("CK_Casos_Precision", "\"PrecisionMetros\" > 0");
            });
            caso.HasKey(c => c.Id);
            caso.Property(c => c.Origen).HasConversion<string>().HasMaxLength(20);
            caso.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20);
            caso.Ignore(c => c.UbicacionDisponible);
            // Idempotencia: una sola fila por ciudadano y solicitud (HU07-CA07).
            caso.HasIndex(c => new { c.CiudadanoId, c.IdSolicitud }).IsUnique();
            caso.HasOne<Usuario>().WithMany().HasForeignKey(c => c.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EventoCaso>(evento =>
        {
            evento.ToTable("EventosCaso");
            evento.HasKey(e => e.Id);
            evento.Property(e => e.Tipo).HasConversion<string>().HasMaxLength(40);
            evento.Property(e => e.Visibilidad).HasConversion<string>().HasMaxLength(20);
            evento.HasOne<Caso>().WithMany().HasForeignKey(e => e.CasoId).OnDelete(DeleteBehavior.Restrict);
            evento.HasOne<Usuario>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
            evento.HasIndex(e => new { e.CasoId, e.OcurridoEn });
        });

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
