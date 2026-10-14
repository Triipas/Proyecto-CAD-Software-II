using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Notificaciones;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Cad.Server.Tests.Casos;

// HU13 y HU20 cambian el mismo caso y comparten su token de concurrencia (xmin). Estas pruebas provocan el
// conflicto de forma determinista: justo antes de guardar, otra conexión modifica la fila del caso.
[Collection(ColeccionServidor.Nombre)]
public class ConflictoConcurrenteTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;

    // Simula que otro operador cambió el caso entre la lectura y el guardado, las veces indicadas.
    private sealed class CambioDeOtroOperador(string conexion, Guid casoId, int veces) : SaveChangesInterceptor
    {
        private int restantes = veces;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData datos,
            InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (restantes-- > 0)
            {
                await using var otra = new NpgsqlConnection(conexion);
                await otra.OpenAsync(ct);
                await using var orden = new NpgsqlCommand("UPDATE \"Casos\" SET \"Estado\" = \"Estado\" WHERE \"Id\" = @id", otra);
                orden.Parameters.AddWithValue("id", casoId);
                await orden.ExecuteNonQueryAsync(ct);
            }
            return resultado;
        }
    }

    private async Task<Guid> SosAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<T> ConConflictosAsync<T>(Guid casoId, int veces, Func<CadDbContext, INotificador, Task<T>> accion)
    {
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        var opciones = new DbContextOptionsBuilder<CadDbContext>()
            .UseNpgsql(conexion)
            .AddInterceptors(new CambioDeOtroOperador(conexion, casoId, veces))
            .Options;
        await using var db = new CadDbContext(opciones);
        using var alcance = fabrica.Services.CreateScope();
        return await accion(db, alcance.ServiceProvider.GetRequiredService<INotificador>());
    }

    // HU24-CA08: un caso «En atención» listo para cerrar (asignado, en camino y en atención por los casos de uso).
    private async Task<(Guid CasoId, Guid SerenoId)> CasoEnAtencionAsync()
    {
        var casoId = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        await fabrica.ConServiciosAsync(async s =>
        {
            Assert.IsType<ResultadoAsignacion.Asignado>(await s.GetRequiredService<AsignarCaso>().EjecutarAsync(casoId, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None));
            return true;
        });
        foreach (var avance in new[] { Avance.ConfirmarDesplazamiento, Avance.IniciarAtencion })
            await fabrica.ConServiciosAsync(s => s.GetRequiredService<AvanzarCaso>().EjecutarAsync(casoId, avance, sereno.Id, ActorAvance.Sereno, CancellationToken.None));
        return (casoId, sereno.Id);
    }

    [Fact]
    public async Task Cerrar_TrasUnCambioConcurrenteDelCaso_VuelveAEvaluarYSeConfirma()
    {
        var (casoId, serenoId) = await CasoEnAtencionAsync();

        var resultado = await ConConflictosAsync(casoId, 1, (db, notificador) =>
            new CerrarCaso(db, TimeProvider.System, notificador, NullLogger<CerrarCaso>.Instance)
                .EjecutarAsync(casoId, new DatosCierre(CategoriaIncidencia.Iniciales[0].Id, "Resultado ficticio", null), serenoId, ActorAvance.Sereno, CancellationToken.None));

        Assert.IsType<ResultadoCierre.Cerrado>(resultado);
        Assert.Equal(EstadoCaso.Cerrada, (await CasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task Cerrar_ConConflictosPersistentes_SeRechazaSinRegistrosParciales()
    {
        var (casoId, serenoId) = await CasoEnAtencionAsync();

        var resultado = await ConConflictosAsync(casoId, int.MaxValue, (db, notificador) =>
            new CerrarCaso(db, TimeProvider.System, notificador, NullLogger<CerrarCaso>.Instance)
                .EjecutarAsync(casoId, new DatosCierre(CategoriaIncidencia.Iniciales[0].Id, "Resultado ficticio", null), serenoId, ActorAvance.Sereno, CancellationToken.None));

        Assert.Equal(CerrarCaso.MotivoCambioConcurrente, Assert.IsType<ResultadoCierre.Rechazado>(resultado).Motivo);
        Assert.Equal(EstadoCaso.EnAtencion, (await CasoAsync(casoId)).Estado);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.CierresCaso.AnyAsync(c => c.CasoId == casoId)));
        Assert.True(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.CasoId == casoId && a.Vigente)));
    }

    // La primera petición de un reenvío confirma justo después de que este comprobara que la solicitud no existía.
    private sealed class PrimeraPeticionConfirma(string conexion, Guid casoId, Guid serenoId, Guid operadorId, Guid idSolicitud)
        : DbCommandInterceptor
    {
        private bool hecho;

        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand comando,
            CommandExecutedEventData datos, System.Data.Common.DbDataReader lector, CancellationToken ct = default)
        {
            if (!hecho && comando.CommandText.Contains("FROM \"Asignaciones\""))
            {
                hecho = true;
                await using var otra = new NpgsqlConnection(conexion);
                await otra.OpenAsync(ct);
                await using var orden = new NpgsqlCommand(
                    "UPDATE \"Casos\" SET \"Estado\" = 'Asignada' WHERE \"Id\" = @caso; " +
                    "INSERT INTO \"Asignaciones\" (\"Id\", \"CasoId\", \"SerenoId\", \"OperadorId\", \"IdSolicitud\", \"AsignadaEn\", \"Vigente\") " +
                    "VALUES (@id, @caso, @sereno, @operador, @solicitud, now(), true)", otra);
                orden.Parameters.AddWithValue("id", Guid.CreateVersion7());
                orden.Parameters.AddWithValue("caso", casoId);
                orden.Parameters.AddWithValue("sereno", serenoId);
                orden.Parameters.AddWithValue("operador", operadorId);
                orden.Parameters.AddWithValue("solicitud", idSolicitud);
                await orden.ExecuteNonQueryAsync(ct);
            }
            return lector;
        }
    }

    // HU20-CA08: un reenvío que pierde la carrera contra su propia primera petición devuelve la asignación ya
    // registrada, no un rechazo («ya no está pendiente» o «el sereno no está disponible»).
    [Fact]
    public async Task Asignar_ReenvioQueLlegaJustoTrasConfirmarLaPrimera_DevuelveLaAsignacionExistente()
    {
        var casoId = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var idSolicitud = Guid.NewGuid();
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        await using var db = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion)
            .AddInterceptors(new PrimeraPeticionConfirma(conexion, casoId, sereno.Id, operador.Id, idSolicitud)).Options);
        using var alcance = fabrica.Services.CreateScope();

        var resultado = await new AsignarCaso(db, TimeProvider.System, alcance.ServiceProvider.GetRequiredService<INotificador>(),
                new ColaAvisosDespacho(), NullLogger<AsignarCaso>.Instance)
            .EjecutarAsync(casoId, sereno.Id, operador.Id, idSolicitud, CancellationToken.None);

        Assert.IsType<ResultadoAsignacion.YaAsignado>(resultado);
        Assert.Single(await fabrica.ConBaseDatosAsync(d => d.Asignaciones.Where(a => a.CasoId == casoId).ToListAsync()));
    }

    // Otro registro del mismo sereno llega primero desde otra conexión (la app abierta dos veces).
    private sealed class RegistroSimultaneo(string conexion, Guid serenoId) : SaveChangesInterceptor
    {
        private bool hecho;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData datos,
            InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (!hecho)
            {
                hecho = true;
                await using var otra = new NpgsqlConnection(conexion);
                await otra.OpenAsync(ct);
                await using var orden = new NpgsqlCommand(
                    "INSERT INTO \"DispositivosSereno\" (\"SerenoId\", \"Token\", \"ActualizadoEn\") VALUES (@id, @token, now())", otra);
                orden.Parameters.AddWithValue("id", serenoId);
                orden.Parameters.AddWithValue("token", $"token-anterior-{Guid.NewGuid():N}");
                await orden.ExecuteNonQueryAsync(ct);
            }
            return resultado;
        }
    }

    [Fact]
    public async Task RegistrarDispositivo_SiOtroRegistroDelMismoSerenoLlegaAntes_ActualizaEnLugarDeFallar()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        await using var db = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>()
            .UseNpgsql(conexion).AddInterceptors(new RegistroSimultaneo(conexion, sereno.Id)).Options);
        var token = $"token-ficticio-{Guid.NewGuid():N}";

        await new Cad.Server.Modulos.Serenos.Aplicacion.GestionarDispositivo(db, TimeProvider.System)
            .RegistrarAsync(sereno.Id, token, CancellationToken.None);

        var guardado = await fabrica.ConBaseDatosAsync(d => d.DispositivosSereno.AsNoTracking().SingleAsync(x => x.SerenoId == sereno.Id));
        Assert.Equal(token, guardado.Token);
    }

    private Task<Caso> CasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    [Fact]
    public async Task Clasificar_TrasUnCambioConcurrenteDelCaso_VuelveAEvaluarYSeConfirma()
    {
        var casoId = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);

        var resultado = await ConConflictosAsync(casoId, 1, (db, notificador) =>
            new ClasificarCaso(db, TimeProvider.System, notificador, NullLogger<ClasificarCaso>.Instance)
                .EjecutarAsync(casoId, Robo, operador.Id, CancellationToken.None));

        Assert.IsType<ResultadoClasificacion.Clasificado>(resultado);
        Assert.Equal(Robo, (await CasoAsync(casoId)).CategoriaId);
    }

    [Fact]
    public async Task Clasificar_ConConflictosPersistentes_SeRechazaSinModificarDatos()
    {
        var casoId = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);

        var resultado = await ConConflictosAsync(casoId, int.MaxValue, (db, notificador) =>
            new ClasificarCaso(db, TimeProvider.System, notificador, NullLogger<ClasificarCaso>.Instance)
                .EjecutarAsync(casoId, Robo, operador.Id, CancellationToken.None));

        Assert.Equal(ClasificarCaso.MotivoCambioConcurrente, Assert.IsType<ResultadoClasificacion.Rechazado>(resultado).Motivo);
        Assert.Null((await CasoAsync(casoId)).CategoriaId);
        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.CasoClasificado)));
    }

    [Fact]
    public async Task Asignar_TrasUnCambioConcurrenteQueNoLoAsigna_VuelveAEvaluarYSeConfirma()
    {
        var casoId = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);

        var resultado = await ConConflictosAsync(casoId, 1, (db, notificador) =>
            new AsignarCaso(db, TimeProvider.System, notificador, new ColaAvisosDespacho(), NullLogger<AsignarCaso>.Instance)
                .EjecutarAsync(casoId, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.IsType<ResultadoAsignacion.Asignado>(resultado);
        Assert.Equal(EstadoCaso.Asignada, (await CasoAsync(casoId)).Estado);
    }

    [Fact]
    public async Task Asignar_ConConflictosPersistentes_SeRechazaSinRegistrosParciales()
    {
        var casoId = await SosAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);

        var resultado = await ConConflictosAsync(casoId, int.MaxValue, (db, notificador) =>
            new AsignarCaso(db, TimeProvider.System, notificador, new ColaAvisosDespacho(), NullLogger<AsignarCaso>.Instance)
                .EjecutarAsync(casoId, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(AsignarCaso.MotivoCambioConcurrente, Assert.IsType<ResultadoAsignacion.Rechazado>(resultado).Motivo);
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.CasoId == casoId)));
    }
}
