using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Serenos;

// HU18 — Activar mi estado operativo y compartir mi ubicación (S2-T02, S2-T03; parte de servidor).
// Datos y coordenadas ficticios. CA04 (efecto en la disponibilidad que ve el operador) se verifica con HU19;
// la parte de CA06 sobre una asignación vigente, con HU20.
[Collection(ColeccionServidor.Nombre)]
public class EstadoOperativoTests(FabricaCad fabrica)
{
    private const string Estado = "/api/sereno/estado";
    private const string Posiciones = "/api/sereno/posiciones";

    private async Task<(HttpClient App, CuentaDePrueba Sereno)> SerenoAsync()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        return (await fabrica.CrearClienteAppAsync(sereno), sereno);
    }

    private static Task<HttpResponseMessage> CambiarAsync(HttpClient app, bool operativo, bool ubicacionAutorizada = true) =>
        app.PutAsJsonAsync(Estado, new SolicitudCambioEstadoOperativo(operativo, ubicacionAutorizada));

    private static PosicionSerenoEnviada Posicion(DateTimeOffset? obtenidaEn = null, double lat = -12.0464, double lon = -77.0428) =>
        new(lat, lon, 8, obtenidaEn ?? DateTimeOffset.UtcNow);

    private Task<int> PosicionesDeAsync(Guid serenoId) =>
        fabrica.ConBaseDatosAsync(db => db.PosicionesSereno.CountAsync(p => p.SerenoId == serenoId));

    [Fact]
    public async Task CA01_ConAutorizacion_SeActivaYSeRecibenPosicionesConSuMarcaDeTiempo()
    {
        var (app, sereno) = await SerenoAsync();

        var activacion = await CambiarAsync(app, operativo: true);
        Assert.Equal(HttpStatusCode.OK, activacion.StatusCode);
        var estado = await activacion.Content.ReadFromJsonAsync<RespuestaEstadoOperativo>();
        Assert.True(estado!.Operativo);
        Assert.NotNull(estado.CambiadoEn);

        var primera = DateTimeOffset.UtcNow.AddSeconds(-30);
        var segunda = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Created, (await app.PostAsJsonAsync(Posiciones, Posicion(primera))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.PostAsJsonAsync(Posiciones, Posicion(segunda, -12.05, -77.03))).StatusCode);

        var guardadas = await fabrica.ConBaseDatosAsync(db => db.PosicionesSereno.AsNoTracking()
            .Where(p => p.SerenoId == sereno.Id).OrderBy(p => p.ObtenidaEn).ToListAsync());
        Assert.Equal(2, guardadas.Count);
        Assert.Equal(primera.UtcDateTime, guardadas[0].ObtenidaEn.UtcDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Equal(-12.05, guardadas[1].Latitud);
        Assert.All(guardadas, p => Assert.InRange(p.RecibidaEn, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public async Task DP15_ElIntervaloEsDe15Segundos() // cambiado por el PO el 22/10/2026 (antes 30 s)
    {
        var (app, _) = await SerenoAsync();

        var estado = await app.GetFromJsonAsync<RespuestaEstadoOperativo>(Estado);

        Assert.Equal(15, estado!.IntervaloPosicionesSegundos);
        Assert.False(estado.Operativo);
        Assert.Null(estado.CambiadoEn); // nunca se activó: no se inventa una hora de cambio
    }

    [Fact]
    public async Task DP15_ElIntervaloEsConfigurable()
    {
        await using var servidor = fabrica.WithWebHostBuilder(b => b.UseSetting("Serenos:IntervaloPosicionesSegundos", "45"));
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = servidor.CreateClient();
        var token = await (await app.PostAsJsonAsync("/api/auth/token",
            new Cad.Contratos.Cuentas.SolicitudInicioSesion(sereno.Correo, sereno.Clave))).Content.ReadFromJsonAsync<Cad.Contratos.Cuentas.RespuestaToken>();
        app.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);

        Assert.Equal(45, (await app.GetFromJsonAsync<RespuestaEstadoOperativo>(Estado))!.IntervaloPosicionesSegundos);
    }

    [Fact]
    public async Task CA02_AlDesactivarse_SeDetieneLaRecepcionDePosiciones()
    {
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, operativo: true);
        await app.PostAsJsonAsync(Posiciones, Posicion());

        var desactivacion = await CambiarAsync(app, operativo: false, ubicacionAutorizada: false);
        var posterior = await app.PostAsJsonAsync(Posiciones, Posicion(DateTimeOffset.UtcNow.AddSeconds(1)));

        Assert.Equal(HttpStatusCode.OK, desactivacion.StatusCode);
        Assert.False((await desactivacion.Content.ReadFromJsonAsync<RespuestaEstadoOperativo>())!.Operativo);
        Assert.Equal(HttpStatusCode.Conflict, posterior.StatusCode);
        Assert.Equal(1, await PosicionesDeAsync(sereno.Id));
    }

    [Fact]
    public async Task CA03_DP16a_SinAutorizacionDeUbicacion_NoSePuedeActivarNiSeRecibenPosiciones()
    {
        var (app, sereno) = await SerenoAsync();

        var activacion = await CambiarAsync(app, operativo: true, ubicacionAutorizada: false);
        var posicion = await app.PostAsJsonAsync(Posiciones, Posicion());

        Assert.Equal(HttpStatusCode.BadRequest, activacion.StatusCode);
        Assert.Contains("debes permitir el acceso a la ubicación", await activacion.Content.ReadAsStringAsync());
        Assert.False((await app.GetFromJsonAsync<RespuestaEstadoOperativo>(Estado))!.Operativo);
        Assert.Equal(HttpStatusCode.Conflict, posicion.StatusCode);
        Assert.Equal(0, await PosicionesDeAsync(sereno.Id));
    }

    [Fact]
    public async Task CA05_OtrosRolesConSuSesion_NoActivanNiTransmitenComoSereno()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var ciudadano = await fabrica.CrearClienteAppAsync(cuenta);

        var activacion = await CambiarAsync(ciudadano, operativo: true);
        var posicion = await ciudadano.PostAsJsonAsync(Posiciones, Posicion());

        Assert.Equal(HttpStatusCode.Forbidden, activacion.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, posicion.StatusCode);
        Assert.Equal(0, await fabrica.ConBaseDatosAsync(db => db.EstadosSereno.CountAsync(e => e.SerenoId == cuenta.Id)));
        Assert.Equal(0, await PosicionesDeAsync(cuenta.Id));
    }

    [Fact]
    public async Task CA06_DP16b_DesactivarseSiempreEstaPermitido()
    {
        // La parte «con asignación vigente» se completa con HU20: este caso de uso no toca asignaciones.
        var (app, _) = await SerenoAsync();
        await CambiarAsync(app, operativo: true);

        var desactivacion = await CambiarAsync(app, operativo: false);

        Assert.Equal(HttpStatusCode.OK, desactivacion.StatusCode);
    }

    [Fact]
    public async Task SinHaberseActivadoNunca_LasPosicionesNoSeRegistran()
    {
        var (app, sereno) = await SerenoAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await app.PostAsJsonAsync(Posiciones, Posicion())).StatusCode);
        Assert.Equal(0, await PosicionesDeAsync(sereno.Id));
    }

    [Fact]
    public async Task RNF08_LaMismaPosicionReenviada_NoSeDuplica()
    {
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, operativo: true);
        var posicion = Posicion();

        var primera = await app.PostAsJsonAsync(Posiciones, posicion);
        var repetidas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => app.PostAsJsonAsync(Posiciones, posicion)));

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.All(repetidas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await PosicionesDeAsync(sereno.Id));
    }

    [Fact]
    public async Task RepetirElMismoEstado_NoCambiaLaHoraDelUltimoCambio()
    {
        var (app, _) = await SerenoAsync();
        var primera = await (await CambiarAsync(app, operativo: true)).Content.ReadFromJsonAsync<RespuestaEstadoOperativo>();

        var segunda = await (await CambiarAsync(app, operativo: true)).Content.ReadFromJsonAsync<RespuestaEstadoOperativo>();

        Assert.Equal(primera!.CambiadoEn, segunda!.CambiadoEn);
    }

    [Fact]
    public async Task PrimeraActivacionSimultanea_DejaUnSoloEstado()
    {
        var (app, sereno) = await SerenoAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CambiarAsync(app, operativo: true)));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.EstadosSereno.CountAsync(e => e.SerenoId == sereno.Id)));
    }

    [Theory]
    [InlineData(95, -77, 8)]
    [InlineData(-12, -181, 8)]
    [InlineData(-12, -77, 0)]
    [InlineData(-12, -77, -3)]
    public async Task PosicionInvalida_SeRechazaSinGuardar(double lat, double lon, double precision)
    {
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, operativo: true);

        var respuesta = await app.PostAsJsonAsync(Posiciones, new PosicionSerenoEnviada(lat, lon, precision, DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, await PosicionesDeAsync(sereno.Id));
    }

    [Fact]
    public async Task PosicionSinMarcaDeTiempo_SeRechaza()
    {
        var (app, sereno) = await SerenoAsync();
        await CambiarAsync(app, operativo: true);

        var respuesta = await app.PostAsJsonAsync(Posiciones, new { latitud = -12.0464, longitud = -77.0428, precisionMetros = 8 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, await PosicionesDeAsync(sereno.Id));
    }
}
