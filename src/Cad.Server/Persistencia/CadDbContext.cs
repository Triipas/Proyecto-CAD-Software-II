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
    public DbSet<Asignacion> Asignaciones => Set<Asignacion>();
    public DbSet<CategoriaIncidencia> CategoriasIncidencia => Set<CategoriaIncidencia>();
    public DbSet<CierreCaso> CierresCaso => Set<CierreCaso>();
    public DbSet<CancelacionSos> CancelacionesSos => Set<CancelacionSos>();
    public DbSet<EvaluacionCaso> EvaluacionesCaso => Set<EvaluacionCaso>();
    public DbSet<DetalleReporte> DetallesReporte => Set<DetalleReporte>();
    public DbSet<CorreccionCategoria> CorreccionesCategoria => Set<CorreccionCategoria>();
    public DbSet<DecisionReporte> DecisionesReporte => Set<DecisionReporte>();
    public DbSet<NotificacionCiudadano> NotificacionesCiudadano => Set<NotificacionCiudadano>();
    public DbSet<Modulos.Serenos.Dominio.EstadoSereno> EstadosSereno => Set<Modulos.Serenos.Dominio.EstadoSereno>();
    public DbSet<Modulos.Serenos.Dominio.PosicionSereno> PosicionesSereno => Set<Modulos.Serenos.Dominio.PosicionSereno>();
    public DbSet<Modulos.Serenos.Dominio.DispositivoSereno> DispositivosSereno => Set<Modulos.Serenos.Dominio.DispositivoSereno>();
    public DbSet<Modulos.Acompanamiento.Dominio.SesionAcompanamiento> SesionesAcompanamiento => Set<Modulos.Acompanamiento.Dominio.SesionAcompanamiento>();
    public DbSet<Modulos.Acompanamiento.Dominio.PosicionAcompanamiento> PosicionesAcompanamiento => Set<Modulos.Acompanamiento.Dominio.PosicionAcompanamiento>();
    public DbSet<Modulos.Acompanamiento.Dominio.ConservacionRecorrido> ConservacionesRecorrido => Set<Modulos.Acompanamiento.Dominio.ConservacionRecorrido>();
    public DbSet<Modulos.Acompanamiento.Dominio.ConfirmacionBienestar> ConfirmacionesBienestar => Set<Modulos.Acompanamiento.Dominio.ConfirmacionBienestar>();
    public DbSet<Modulos.Mediciones.MedicionPresentacion> MedicionesPresentacion => Set<Modulos.Mediciones.MedicionPresentacion>();

    // HU31-CA01/CA05: cada cambio del estado público de un caso genera, en la misma transacción, una notificación para
    // su titular; un cambio interno que no cambia el estado público (en camino, inicio de la atención) no la genera.
    // Registrar un caso no notifica: es el propio ciudadano quien lo crea; tampoco cancelarlo (HU09).
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NotificarCambiosPublicos();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NotificarCambiosPublicos();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NotificarCambiosPublicos()
    {
        var ahora = DateTimeOffset.UtcNow;
        foreach (var entrada in ChangeTracker.Entries<Caso>().Where(e => e.State == EntityState.Modified).ToList())
        {
            var estado = entrada.Property(c => c.Estado);
            if (!estado.IsModified)
                continue;
            var caso = entrada.Entity;
            var antes = EstadoPublicoCaso.De(caso.Origen, estado.OriginalValue);
            var despues = EstadoPublicoCaso.De(caso.Origen, estado.CurrentValue);
            if (antes != despues && despues != Cad.Contratos.Casos.EstadosPublicos.Cancelado)
                NotificacionesCiudadano.Add(NotificacionCiudadano.PorCambioDeEstado(caso, despues, ahora));
        }
    }

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
                // HU13-CA01: una clasificación guarda siempre categoría, responsable y momento juntos.
                tabla.HasCheckConstraint("CK_Casos_ClasificacionCompleta",
                    "(\"CategoriaId\" IS NULL AND \"ClasificadoPor\" IS NULL AND \"ClasificadoEn\" IS NULL) OR " +
                    "(\"CategoriaId\" IS NOT NULL AND \"ClasificadoPor\" IS NOT NULL AND \"ClasificadoEn\" IS NOT NULL)");
            });
            caso.HasKey(c => c.Id);
            caso.Property(c => c.Origen).HasConversion<string>().HasMaxLength(20);
            caso.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20);
            caso.Ignore(c => c.UbicacionDisponible);
            caso.Ignore(c => c.PuedeAsignarse);
            caso.Ignore(c => c.PuedeCancelarse);
            caso.Ignore(c => c.Clasificado);
            // Npgsql asocia un uint marcado como versión a la columna del sistema xmin (sin columna nueva).
            caso.Property(c => c.Version).IsRowVersion();
            // Idempotencia: una sola fila por ciudadano y solicitud (HU07-CA07).
            caso.HasIndex(c => new { c.CiudadanoId, c.IdSolicitud }).IsUnique();
            caso.HasOne<Usuario>().WithMany().HasForeignKey(c => c.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
            // HU13-CA04: solo una categoría del catálogo (la base de datos lo garantiza aunque falle el código).
            caso.HasOne<CategoriaIncidencia>().WithMany().HasForeignKey(c => c.CategoriaId).OnDelete(DeleteBehavior.Restrict);
            caso.HasOne<Usuario>().WithMany().HasForeignKey(c => c.ClasificadoPor).OnDelete(DeleteBehavior.Restrict);
        });

        // DP07: catálogo compartido de categorías de incidencia (HU10, HU13, HU14, HU24).
        builder.Entity<CategoriaIncidencia>(categoria =>
        {
            categoria.ToTable("CategoriasIncidencia");
            categoria.HasKey(c => c.Id);
            categoria.Property(c => c.Nombre).HasMaxLength(100);
            categoria.HasIndex(c => c.Nombre).IsUnique();
            categoria.HasData(CategoriaIncidencia.Iniciales.Select(c => new { c.Id, c.Nombre, c.Orden }));
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

        // HU24: un cierre por caso (CA08). Tipificación del catálogo (DP07) y resultado obligatorio (CA02), también
        // garantizados por la base de datos; las notas internas, en una columna aparte del resultado público (CA04).
        builder.Entity<CierreCaso>(cierre =>
        {
            cierre.ToTable("CierresCaso", tabla =>
                tabla.HasCheckConstraint("CK_CierresCaso_Resultado", "length(btrim(\"ResultadoPublico\")) > 0"));
            cierre.HasKey(c => c.CasoId);
            cierre.Property(c => c.ResultadoPublico).HasMaxLength(CierreCaso.MaximoResultado);
            cierre.Property(c => c.NotasInternas).HasMaxLength(CierreCaso.MaximoNotas);
            cierre.HasOne<Caso>().WithOne().HasForeignKey<CierreCaso>(c => c.CasoId).OnDelete(DeleteBehavior.Restrict);
            cierre.HasOne<CategoriaIncidencia>().WithMany().HasForeignKey(c => c.CategoriaId).OnDelete(DeleteBehavior.Restrict);
            cierre.HasOne<Usuario>().WithMany().HasForeignKey(c => c.ResponsableId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU09: una cancelación por caso (CA08), con el motivo obligatorio (CA01, CA04) también garantizado por la base de datos.
        builder.Entity<CancelacionSos>(cancelacion =>
        {
            cancelacion.ToTable("CancelacionesSos", tabla =>
                tabla.HasCheckConstraint("CK_CancelacionesSos_Motivo", "\"Motivo\" IS NOT NULL AND length(btrim(\"Motivo\")) > 0"));
            cancelacion.HasKey(c => c.CasoId);
            cancelacion.Property(c => c.Motivo).HasMaxLength(CancelacionSos.MaximoMotivo);
            cancelacion.HasOne<Caso>().WithOne().HasForeignKey<CancelacionSos>(c => c.CasoId).OnDelete(DeleteBehavior.Restrict);
            cancelacion.HasOne<Usuario>().WithMany().HasForeignKey(c => c.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU32: una evaluación por caso (CA03), con la puntuación de 1 a 5 (CA02) también garantizada por la base de datos.
        builder.Entity<EvaluacionCaso>(evaluacion =>
        {
            evaluacion.ToTable("EvaluacionesCaso", tabla =>
                tabla.HasCheckConstraint("CK_EvaluacionesCaso_Puntuacion", "\"Puntuacion\" IS NOT NULL AND \"Puntuacion\" BETWEEN 1 AND 5"));
            evaluacion.HasKey(e => e.CasoId);
            evaluacion.Property(e => e.Comentario).HasMaxLength(EvaluacionCaso.MaximoComentario);
            evaluacion.HasOne<Caso>().WithOne().HasForeignKey<EvaluacionCaso>(e => e.CasoId).OnDelete(DeleteBehavior.Restrict);
            evaluacion.HasOne<Usuario>().WithMany().HasForeignKey(e => e.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU10: datos del reporte, uno por caso. La base de datos garantiza los campos obligatorios (CA02) y que la
        // ubicación del hecho sea un punto completo o una referencia sin coordenadas (CA03, CA04; DP10).
        builder.Entity<DetalleReporte>(detalle =>
        {
            detalle.ToTable("DetallesReporte", tabla =>
            {
                tabla.HasCheckConstraint("CK_DetallesReporte_Descripcion", "length(btrim(\"Descripcion\")) > 0");
                tabla.HasCheckConstraint("CK_DetallesReporte_Ubicacion",
                    "(\"FuenteUbicacion\" = 'Texto' AND \"LatitudHecho\" IS NULL AND \"LongitudHecho\" IS NULL AND \"PrecisionHechoMetros\" IS NULL " +
                    "AND \"ReferenciaHecho\" IS NOT NULL AND length(btrim(\"ReferenciaHecho\")) > 0) OR " +
                    "(\"FuenteUbicacion\" IN ('Mapa', 'PosicionActual') AND \"LatitudHecho\" IS NOT NULL AND \"LongitudHecho\" IS NOT NULL)");
                tabla.HasCheckConstraint("CK_DetallesReporte_Latitud", "\"LatitudHecho\" BETWEEN -90 AND 90");
                tabla.HasCheckConstraint("CK_DetallesReporte_Longitud", "\"LongitudHecho\" BETWEEN -180 AND 180");
                tabla.HasCheckConstraint("CK_DetallesReporte_Precision", "\"PrecisionHechoMetros\" > 0");
            });
            detalle.HasKey(d => d.CasoId);
            detalle.Property(d => d.Descripcion).HasMaxLength(DetalleReporte.MaximoDescripcion);
            detalle.Property(d => d.ReferenciaHecho).HasMaxLength(DetalleReporte.MaximoReferencia);
            detalle.Property(d => d.FuenteUbicacion).HasMaxLength(20);
            detalle.HasOne<Caso>().WithOne().HasForeignKey<DetalleReporte>(d => d.CasoId).OnDelete(DeleteBehavior.Restrict);
            // CA07: la categoría original también es del catálogo.
            detalle.HasOne<CategoriaIncidencia>().WithMany().HasForeignKey(d => d.CategoriaOriginalId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU14-CA03: correcciones de categoría (solo inserción), ambas del catálogo y con su responsable.
        builder.Entity<CorreccionCategoria>(correccion =>
        {
            correccion.ToTable("CorreccionesCategoria", tabla =>
                tabla.HasCheckConstraint("CK_CorreccionesCategoria_Cambio", "\"CategoriaAnteriorId\" <> \"CategoriaNuevaId\""));
            correccion.HasKey(c => c.Id);
            correccion.HasIndex(c => new { c.CasoId, c.CorregidaEn });
            correccion.HasOne<Caso>().WithMany().HasForeignKey(c => c.CasoId).OnDelete(DeleteBehavior.Restrict);
            correccion.HasOne<CategoriaIncidencia>().WithMany().HasForeignKey(c => c.CategoriaAnteriorId).OnDelete(DeleteBehavior.Restrict);
            correccion.HasOne<CategoriaIncidencia>().WithMany().HasForeignKey(c => c.CategoriaNuevaId).OnDelete(DeleteBehavior.Restrict);
            correccion.HasOne<Usuario>().WithMany().HasForeignKey(c => c.OperadorId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU15–HU17: una decisión por reporte (la clave es el caso: dos decisiones a la vez no pueden quedar ambas).
        // La base de datos exige el motivo y, en la derivación, el destino y la acción (HU16-CA02).
        builder.Entity<DecisionReporte>(decision =>
        {
            decision.ToTable("DecisionesReporte", tabla =>
            {
                tabla.HasCheckConstraint("CK_DecisionesReporte_Motivo", "length(btrim(\"Motivo\")) > 0");
                tabla.HasCheckConstraint("CK_DecisionesReporte_Derivacion",
                    // IS NOT NULL explícito: con un valor nulo, length(btrim(NULL)) > 0 es NULL y un CHECK con NULL se acepta.
                    "(\"Tipo\" = 'Derivacion' AND \"Destino\" IS NOT NULL AND \"Accion\" IS NOT NULL " +
                    "AND length(btrim(\"Destino\")) > 0 AND length(btrim(\"Accion\")) > 0) OR " +
                    "(\"Tipo\" IN ('Admision', 'Descarte') AND \"Destino\" IS NULL AND \"Accion\" IS NULL)");
            });
            decision.HasKey(d => d.CasoId);
            decision.Property(d => d.Tipo).HasConversion<string>().HasMaxLength(20);
            decision.Property(d => d.Motivo).HasMaxLength(DecisionReporte.MaximoMotivo);
            decision.Property(d => d.Destino).HasMaxLength(DecisionReporte.MaximoDestino);
            decision.Property(d => d.Accion).HasMaxLength(DecisionReporte.MaximoAccion);
            decision.HasOne<Caso>().WithOne().HasForeignKey<DecisionReporte>(d => d.CasoId).OnDelete(DeleteBehavior.Restrict);
            decision.HasOne<Usuario>().WithMany().HasForeignKey(d => d.OperadorId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU31: bandeja de notificaciones del ciudadano (persistente: CA02). Solo inserción, salvo la marca de lectura.
        builder.Entity<NotificacionCiudadano>(notificacion =>
        {
            notificacion.ToTable("NotificacionesCiudadano");
            notificacion.HasKey(n => n.Id);
            notificacion.Property(n => n.EstadoPublico).HasMaxLength(30);
            notificacion.Property(n => n.Texto).HasMaxLength(200);
            notificacion.HasIndex(n => new { n.CiudadanoId, n.CreadaEn });
            notificacion.HasOne<Caso>().WithMany().HasForeignKey(n => n.CasoId).OnDelete(DeleteBehavior.Restrict);
            notificacion.HasOne<Usuario>().WithMany().HasForeignKey(n => n.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU20: asignaciones. Una sola vigente por caso y una sola vigente por sereno (CA04, CA05, RNF08).
        builder.Entity<Asignacion>(asignacion =>
        {
            asignacion.ToTable("Asignaciones");
            asignacion.HasKey(a => a.Id);
            asignacion.HasIndex(a => a.CasoId).IsUnique().HasFilter("\"Vigente\"").HasDatabaseName("IX_Asignaciones_CasoVigente");
            asignacion.HasIndex(a => a.SerenoId).IsUnique().HasFilter("\"Vigente\"").HasDatabaseName("IX_Asignaciones_SerenoVigente");
            // La misma solicitud reenviada no crea una segunda asignación (CA08).
            asignacion.HasIndex(a => new { a.OperadorId, a.IdSolicitud }).IsUnique();
            asignacion.HasOne<Caso>().WithMany().HasForeignKey(a => a.CasoId).OnDelete(DeleteBehavior.Restrict);
            asignacion.HasOne<Usuario>().WithMany().HasForeignKey(a => a.SerenoId).OnDelete(DeleteBehavior.Restrict);
            asignacion.HasOne<Usuario>().WithMany().HasForeignKey(a => a.OperadorId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU18: estado operativo (una fila por sereno) y posiciones recibidas (solo inserción).
        builder.Entity<Modulos.Serenos.Dominio.EstadoSereno>(estado =>
        {
            estado.ToTable("EstadosSereno");
            estado.HasKey(e => e.SerenoId);
            estado.HasOne<Usuario>().WithOne().HasForeignKey<Modulos.Serenos.Dominio.EstadoSereno>(e => e.SerenoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // HU21 (ADR-07): dispositivo para la notificación push, uno por sereno. Un mismo token no puede quedar
        // asociado a dos serenos (el teléfono lo usa quien ingresó por última vez).
        builder.Entity<Modulos.Serenos.Dominio.DispositivoSereno>(dispositivo =>
        {
            dispositivo.ToTable("DispositivosSereno");
            dispositivo.HasKey(d => d.SerenoId);
            dispositivo.Property(d => d.Token).HasMaxLength(Modulos.Serenos.Dominio.DispositivoSereno.MaximoToken);
            dispositivo.HasIndex(d => d.Token).IsUnique();
            dispositivo.HasOne<Usuario>().WithOne().HasForeignKey<Modulos.Serenos.Dominio.DispositivoSereno>(d => d.SerenoId)
                .OnDelete(DeleteBehavior.Cascade);
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

        // HU34–HU37: sesiones de acompañamiento. Una sola activa por ciudadano (HU34-CA06, índice único parcial) y una por
        // pulsación (IdSolicitud). La base de datos exige que una sesión cerrada tenga su hora de cierre y una activa no.
        builder.Entity<Modulos.Acompanamiento.Dominio.SesionAcompanamiento>(sesion =>
        {
            sesion.ToTable("SesionesAcompanamiento", tabla =>
            {
                tabla.HasCheckConstraint("CK_SesionesAcompanamiento_Vencimiento", "\"VenceEn\" > \"IniciadaEn\"");
                // IS NOT NULL explícito: un CHECK con NULL se acepta.
                tabla.HasCheckConstraint("CK_SesionesAcompanamiento_Cierre",
                    "(\"Estado\" = 'Activa' AND \"CerradaEn\" IS NULL) OR " +
                    "(\"Estado\" IN ('Finalizada', 'Vencida') AND \"CerradaEn\" IS NOT NULL AND \"CerradaEn\" >= \"IniciadaEn\")");
            });
            sesion.HasKey(s => s.Id);
            sesion.Property(s => s.Estado).HasConversion<string>().HasMaxLength(20);
            sesion.Property(s => s.Version).IsRowVersion();
            sesion.HasIndex(s => new { s.CiudadanoId, s.IdSolicitud }).IsUnique();
            sesion.HasIndex(s => s.CiudadanoId).IsUnique().HasFilter("\"Estado\" = 'Activa'").HasDatabaseName("IX_SesionesAcompanamiento_Activa");
            sesion.HasIndex(s => new { s.Estado, s.VenceEn });
            sesion.HasOne<Usuario>().WithMany().HasForeignKey(s => s.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU35: posiciones del acompañamiento (solo inserción). Un reenvío de la misma posición no la duplica.
        builder.Entity<Modulos.Acompanamiento.Dominio.PosicionAcompanamiento>(posicion =>
        {
            posicion.ToTable("PosicionesAcompanamiento", tabla =>
            {
                tabla.HasCheckConstraint("CK_PosicionesAcompanamiento_Latitud", "\"Latitud\" BETWEEN -90 AND 90");
                tabla.HasCheckConstraint("CK_PosicionesAcompanamiento_Longitud", "\"Longitud\" BETWEEN -180 AND 180");
                tabla.HasCheckConstraint("CK_PosicionesAcompanamiento_Precision", "\"PrecisionMetros\" > 0");
            });
            posicion.HasKey(p => p.Id);
            posicion.HasIndex(p => new { p.SesionId, p.ObtenidaEn }).IsUnique();
            posicion.HasOne<Modulos.Acompanamiento.Dominio.SesionAcompanamiento>().WithMany().HasForeignKey(p => p.SesionId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU40 y HU41: confirmaciones de bienestar. DP20 (decisión del PO del 16/10/2026): una sola pendiente por sesión, también
        // garantizado por la base de datos. Una respuesta guarda siempre su momento, y solo «Necesito ayuda» lleva un SOS.
        builder.Entity<Modulos.Acompanamiento.Dominio.ConfirmacionBienestar>(confirmacion =>
        {
            confirmacion.ToTable("ConfirmacionesBienestar", tabla =>
            {
                tabla.HasCheckConstraint("CK_ConfirmacionesBienestar_Respuesta",
                    "(\"Respuesta\" IS NULL AND \"RespondidaEn\" IS NULL AND \"CasoId\" IS NULL) OR " +
                    "(\"Respuesta\" = 'EstoyBien' AND \"RespondidaEn\" IS NOT NULL AND \"CasoId\" IS NULL) OR " +
                    "(\"Respuesta\" = 'NecesitoAyuda' AND \"RespondidaEn\" IS NOT NULL AND \"CasoId\" IS NOT NULL)");
            });
            confirmacion.HasKey(c => c.Id);
            confirmacion.Property(c => c.Respuesta).HasConversion<string>().HasMaxLength(20);
            confirmacion.Property(c => c.Version).IsRowVersion();
            confirmacion.Ignore(c => c.Pendiente);
            confirmacion.HasIndex(c => new { c.OperadorId, c.IdSolicitud }).IsUnique();
            confirmacion.HasIndex(c => c.SesionId).IsUnique().HasFilter("\"Respuesta\" IS NULL").HasDatabaseName("IX_ConfirmacionesBienestar_Pendiente");
            confirmacion.HasOne<Modulos.Acompanamiento.Dominio.SesionAcompanamiento>().WithMany().HasForeignKey(c => c.SesionId).OnDelete(DeleteBehavior.Restrict);
            confirmacion.HasOne<Usuario>().WithMany().HasForeignKey(c => c.CiudadanoId).OnDelete(DeleteBehavior.Restrict);
            confirmacion.HasOne<Usuario>().WithMany().HasForeignKey(c => c.OperadorId).OnDelete(DeleteBehavior.Restrict);
            confirmacion.HasOne<Caso>().WithMany().HasForeignKey(c => c.CasoId).OnDelete(DeleteBehavior.Restrict);
        });

        // HU39-B (CA05): conservación excepcional de un recorrido, una por sesión, con su caso, su finalidad y su responsable.
        builder.Entity<Modulos.Acompanamiento.Dominio.ConservacionRecorrido>(conservacion =>
        {
            conservacion.ToTable("ConservacionesRecorrido", tabla =>
                // IS NOT NULL explícito: un CHECK con NULL se acepta.
                tabla.HasCheckConstraint("CK_ConservacionesRecorrido_Finalidad", "\"Finalidad\" IS NOT NULL AND length(btrim(\"Finalidad\")) > 0"));
            conservacion.HasKey(c => c.SesionId);
            conservacion.Property(c => c.Finalidad).HasMaxLength(Modulos.Acompanamiento.Dominio.ConservacionRecorrido.MaximoFinalidad);
            conservacion.HasOne<Modulos.Acompanamiento.Dominio.SesionAcompanamiento>().WithOne()
                .HasForeignKey<Modulos.Acompanamiento.Dominio.ConservacionRecorrido>(c => c.SesionId).OnDelete(DeleteBehavior.Restrict);
            conservacion.HasOne<Caso>().WithMany().HasForeignKey(c => c.CasoId).OnDelete(DeleteBehavior.Restrict);
            conservacion.HasOne<Usuario>().WithMany().HasForeignKey(c => c.OperadorId).OnDelete(DeleteBehavior.Restrict);
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
