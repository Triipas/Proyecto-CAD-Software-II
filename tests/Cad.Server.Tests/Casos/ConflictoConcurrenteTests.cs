using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
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
            new AsignarCaso(db, TimeProvider.System, notificador, NullLogger<AsignarCaso>.Instance)
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
            new AsignarCaso(db, TimeProvider.System, notificador, NullLogger<AsignarCaso>.Instance)
                .EjecutarAsync(casoId, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(AsignarCaso.MotivoCambioConcurrente, Assert.IsType<ResultadoAsignacion.Rechazado>(resultado).Motivo);
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(casoId)).Estado);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.CasoId == casoId)));
    }
}
