using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Casos;

// HU07 — Emitir un SOS con una única activación (S1-T13, S1-T14) y
// HU08 — Asociar la ubicación actual al SOS (parte de servidor de S1-T15). Datos y coordenadas ficticios.
// La activación única en la interfaz (HU07-CA01), la confirmación visible (CA03) y la captura de la
// posición (HU08-CA04/CA05) pertenecen a la app y se verifican con S1-T16.
[Collection(ColeccionServidor.Nombre)]
public class SosTests(FabricaCad fabrica)
{
    private const string Ruta = "/api/sos";

    private static readonly DateTimeOffset Activacion = new(2026, 10, 12, 21, 30, 0, TimeSpan.FromHours(-5));

    private static UbicacionSos UbicacionActual(double lat = -12.0464, double lon = -77.0428, double precision = 12.5) =>
        new(lat, lon, precision, Activacion.AddSeconds(3));

    private async Task<(HttpClient App, CuentaDePrueba Ciudadano)> CiudadanoAsync()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        return (await fabrica.CrearClienteAppAsync(ciudadano), ciudadano);
    }

    private Task<List<Caso>> CasosDeAsync(Guid ciudadanoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().Where(c => c.CiudadanoId == ciudadanoId).ToListAsync());

    [Fact]
    public async Task HU07_CA01_SoloRequiereLaActivacion_SinFormularioCategoriaNiDescripcion()
    {
        var (app, _) = await CiudadanoAsync();

        // El cuerpo mínimo: el identificador de la activación y su momento; nada más.
        var respuesta = await app.PostAsJsonAsync(Ruta, new { idSolicitud = Guid.NewGuid(), activadoEn = Activacion });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Fact]
    public async Task HU07_CA02_RegistraIdentificadorEmisorFechaHoraYEstadoPendiente()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, UbicacionActual()));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaSos>();
        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(cuerpo!.Id, caso.Id);
        Assert.Equal(OrigenCaso.Sos, caso.Origen);
        Assert.Equal(EstadoCaso.Pendiente, caso.Estado);
        Assert.Equal("Pendiente", cuerpo.Estado);
        Assert.InRange(caso.RegistradoEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        // El historial nace en la misma transacción.
        var eventos = await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking().Where(e => e.CasoId == caso.Id).ToListAsync());
        var evento = Assert.Single(eventos);
        Assert.Equal(TipoEventoCaso.SosEmitido, evento.Tipo);
        Assert.Equal(ciudadano.Id, evento.ActorId);
    }

    [Fact]
    public async Task HU07_CA04_SolicitudRechazada_NoDejaUnSosParcial()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var eventosAntes = await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync());

        var respuesta = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.Empty, Activacion, UbicacionActual()));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(eventosAntes, await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync()));
    }

    [Fact]
    public async Task HU07_CA05_EmitirSos_NoCreaOtrosCasosNiSesiones()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null));

        // Un único caso, de origen SOS (ni un reporte ni una sesión de acompañamiento).
        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(OrigenCaso.Sos, caso.Origen);
    }

    [Fact]
    public async Task HU07_CA06_ConYSinUbicacion_ElSosSeRegistraIgual()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var conUbicacion = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, UbicacionActual()));
        var sinUbicacion = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null));

        Assert.Equal(HttpStatusCode.Created, conUbicacion.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sinUbicacion.StatusCode);
        Assert.Equal(2, (await CasosDeAsync(ciudadano.Id)).Count);
    }

    [Fact]
    public async Task HU07_CA07_ReiteracionTecnica_RegistraUnSoloSosYDevuelveElMismo()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var solicitud = new SolicitudEmisionSos(Guid.NewGuid(), Activacion, UbicacionActual());

        var primera = await app.PostAsJsonAsync(Ruta, solicitud);
        var reintento = await app.PostAsJsonAsync(Ruta, solicitud);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        Assert.Equal((await primera.Content.ReadFromJsonAsync<RespuestaSos>())!.Id,
            (await reintento.Content.ReadFromJsonAsync<RespuestaSos>())!.Id);
        Assert.Single(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task HU07_CA07_ReintentosSimultaneos_RegistranUnSoloSos()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var solicitud = new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => app.PostAsJsonAsync(Ruta, solicitud)));

        Assert.All(respuestas, r => Assert.True(r.IsSuccessStatusCode, $"Respuesta {(int)r.StatusCode}"));
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Created));
        var ids = await Task.WhenAll(respuestas.Select(async r => (await r.Content.ReadFromJsonAsync<RespuestaSos>())!.Id));
        Assert.Single(ids.Distinct());
        Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.ActorId == ciudadano.Id)));
    }

    [Fact]
    public async Task DP14_ActivacionesDistintas_CreanSosDistintosAunqueHayaUnoPendiente()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var primero = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null));
        var segundo = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion.AddMinutes(1), null));

        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segundo.StatusCode);
        Assert.Equal(2, (await CasosDeAsync(ciudadano.Id)).Count);
    }

    [Fact]
    public async Task HU08_CA01_UbicacionDisponible_AsociaCoordenadasMomentoYPrecision()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var ubicacion = UbicacionActual(-12.1219, -77.0297, 8.4);

        var respuesta = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, ubicacion));

        Assert.True((await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.UbicacionDisponible);
        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(-12.1219, caso.Latitud);
        Assert.Equal(-77.0297, caso.Longitud);
        Assert.Equal(8.4, caso.PrecisionMetros);
        Assert.Equal(ubicacion.ObtenidaEn, caso.UbicacionObtenidaEn);
    }

    [Fact]
    public async Task HU08_CA02_SinUbicacion_QuedaComoNoDisponibleYSinCoordenadas()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, null));

        Assert.False((await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.UbicacionDisponible);
        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Null(caso.Latitud);
        Assert.Null(caso.Longitud);
        Assert.Null(caso.PrecisionMetros);
        Assert.Null(caso.UbicacionObtenidaEn);
    }

    [Fact]
    public async Task HU08_CA03_PosicionAnteriorALaActivacion_NoSePresentaComoActual()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        // Por ejemplo, la posición de la comprobación inicial de HU06, obtenida minutos antes.
        var anterior = new UbicacionSos(-12.0464, -77.0428, 10, Activacion.AddMinutes(-4));

        var respuesta = await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, anterior));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode); // el SOS se registra igual
        Assert.False((await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.UbicacionDisponible);
        Assert.Null(Assert.Single(await CasosDeAsync(ciudadano.Id)).Latitud);
    }

    [Theory]
    [InlineData(95, -77, 10)]     // latitud fuera de rango
    [InlineData(-12, -190, 10)]   // longitud fuera de rango
    [InlineData(-12, -77, 0)]     // precisión no positiva
    public async Task UbicacionInvalida_NoBloqueaElSos_SeRegistraSinUbicacion(double lat, double lon, double precision)
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync(Ruta,
            new SolicitudEmisionSos(Guid.NewGuid(), Activacion, new UbicacionSos(lat, lon, precision, Activacion.AddSeconds(2))));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.False((await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.UbicacionDisponible);
        Assert.Null(Assert.Single(await CasosDeAsync(ciudadano.Id)).Latitud);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Operador)]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Administrador)]
    public async Task HU03_SolicitudDirectaDeOtroPerfil_NoEmiteSos(string? rol)
    {
        HttpClient cliente;
        if (rol is null)
            cliente = fabrica.CreateClient();
        else if (rol == Roles.Sereno)
            cliente = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));
        else
        {
            // Operador y administrador: sesión del panel (cookie), que también vale para /api.
            var cuenta = rol == Roles.Administrador
                ? new CuentaDePrueba(Guid.Empty, fabrica.AdminCorreo, fabrica.AdminClave, rol)
                : await fabrica.CrearAsync(rol);
            cliente = fabrica.CrearNavegador();
            await cliente.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        }
        var casosAntes = await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync());

        var respuesta = await cliente.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, UbicacionActual()));

        Assert.True(respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Se esperaba 401/403 y se obtuvo {(int)respuesta.StatusCode}");
        Assert.Equal(casosAntes, await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync()));
    }

    [Fact]
    public async Task RNF07_LasCoordenadasDelSosNoAparecenEnLosRegistros()
    {
        var (app, _) = await CiudadanoAsync();
        var ubicacion = UbicacionActual(-12.0987654, -77.0123456, 7.25);

        await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, ubicacion));
        await app.PostAsJsonAsync(Ruta, new SolicitudEmisionSos(Guid.NewGuid(), Activacion, ubicacion with { Latitud = 200 }));

        foreach (var dato in new[] { ubicacion.Latitud, ubicacion.Longitud }.Select(v => v.ToString(CultureInfo.InvariantCulture)))
            Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(dato));
    }
}
