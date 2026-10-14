using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Persistencia;

public class CadDbContext(DbContextOptions<CadDbContext> options)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    // Claves que protegen las cookies del panel y los tokens antifalsificación. En la base de datos
    // sobreviven a cada despliegue (el sistema de archivos del contenedor es efímero; Plan de Arquitectura, Tabla 21).
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Caso> Casos => Set<Caso>();
    public DbSet<EventoCaso> EventosCaso => Set<EventoCaso>();
    public DbSet<IntentoContacto> IntentosContacto => Set<IntentoContacto>();
    public DbSet<Modulos.Serenos.Dominio.EstadoSereno> EstadosSereno => Set<Modulos.Serenos.Dominio.EstadoSereno>();
    public DbSet<Modulos.Serenos.Dominio.PosicionSereno> PosicionesSereno => Set<Modulos.Serenos.Dominio.PosicionSereno>();
    public DbSet<Modulos.Mediciones.MedicionPresentacion> MedicionesPresentacion => Set<Modulos.Mediciones.MedicionPresentacion>();

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

        // RNF02: tabla de medición, solo para pruebas (Plan de Arquitectura, 9.1).
        builder.Entity<Modulos.Mediciones.MedicionPresentacion>(medicion =>
        {
            medicion.ToTable("MedicionesPresentacion");
            medicion.HasKey(m => m.Id);
            medicion.HasIndex(m => new { m.CasoId, m.OperadorId }).IsUnique();
            medicion.HasIndex(m => m.AceptadoEn);
            medicion.HasOne<Caso>().WithMany().HasForeignKey(m => m.CasoId).OnDelete(DeleteBehavior.Cascade);
            medicion.HasOne<Usuario>().WithMany().HasForeignKey(m => m.OperadorId).OnDelete(DeleteBehavior.Cascade);
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

        // HU12: intentos de contacto, solo inserción (CA07).
        builder.Entity<IntentoContacto>(intento =>
        {
            // CA03: la base de datos tampoco admite un resultado distinto de los tres definidos.
            intento.ToTable("IntentosContacto", tabla => tabla.HasCheckConstraint("CK_IntentosContacto_Resultado",
                "\"Resultado\" IN ('Contactado', 'SinRespuesta', 'NoFuePosibleComunicar')"));
            intento.HasKey(i => i.Id);
            intento.Property(i => i.Resultado).HasConversion<string>().HasMaxLength(30);
            intento.Property(i => i.InformacionHecho).HasMaxLength(IntentoContacto.MaximoInformacion);
            intento.Property(i => i.ReferenciaUbicacion).HasMaxLength(IntentoContacto.MaximoReferencia);
            // Un reenvío del mismo formulario no crea un segundo intento (RNF08).
            intento.HasIndex(i => new { i.CasoId, i.IdSolicitud }).IsUnique();
            intento.HasIndex(i => new { i.CasoId, i.RegistradoEn });
            intento.HasOne<Caso>().WithMany().HasForeignKey(i => i.CasoId).OnDelete(DeleteBehavior.Restrict);
            intento.HasOne<Usuario>().WithMany().HasForeignKey(i => i.OperadorId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU18: estado operativo (una fila por sereno) y posiciones recibidas (solo inserción).
        builder.Entity<Modulos.Serenos.Dominio.EstadoSereno>(estado =>
        {
            estado.ToTable("EstadosSereno");
            estado.HasKey(e => e.SerenoId);
            estado.HasOne<Usuario>().WithOne().HasForeignKey<Modulos.Serenos.Dominio.EstadoSereno>(e => e.SerenoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Modulos.Serenos.Dominio.PosicionSereno>(posicion =>
        {
            posicion.ToTable("PosicionesSereno", tabla =>
            {
                tabla.HasCheckConstraint("CK_PosicionesSereno_Latitud", "\"Latitud\" BETWEEN -90 AND 90");
                tabla.HasCheckConstraint("CK_PosicionesSereno_Longitud", "\"Longitud\" BETWEEN -180 AND 180");
                tabla.HasCheckConstraint("CK_PosicionesSereno_Precision", "\"PrecisionMetros\" > 0");
            });
            posicion.HasKey(p => p.Id);
            // Un reenvío de la misma posición no la duplica; también sirve para leer la última posición (HU19).
            posicion.HasIndex(p => new { p.SerenoId, p.ObtenidaEn }).IsUnique();
            posicion.HasOne<Usuario>().WithMany().HasForeignKey(p => p.SerenoId).OnDelete(DeleteBehavior.Restrict);
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
