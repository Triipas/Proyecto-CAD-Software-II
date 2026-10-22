using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cad.Server.Tests.Casos;

// HU14 — Iniciar la revisión de un reporte y corregir su categoría (S3-T05). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class RevisionReporteTests(FabricaCad fabrica)
{
    private static readonly Guid ViolenciaDomestica = CategoriaIncidencia.Iniciales[0].Id;
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static readonly Guid Otros = CategoriaIncidencia.Iniciales[3].Id;

    private static string Ruta(Guid id) => $"/panel/reportes/{id}";

    private async Task<Guid> ReporteAsync(Guid? categoria = null, string descripcion = "Robo de una bicicleta (ficticio)")
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), categoria ?? Robo, descripcion,
            DateTimeOffset.UtcNow.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0631, -77.0365, null, "Frente al mercado")));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient navegador, Guid id, string manejador, Dictionary<string, string>? campos = null)
    {
        campos ??= [];
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, Ruta(id));
        return await navegador.PostAsync($"{Ruta(id)}?handler={manejador}", new FormUrlEncodedContent(campos));
    }

    private static Task<HttpResponseMessage> IniciarAsync(HttpClient navegador, Guid id) => EnviarAsync(navegador, id, "IniciarRevision");

    private static Task<HttpResponseMessage> CorregirAsync(HttpClient navegador, Guid id, Guid categoria) =>
        EnviarAsync(navegador, id, "Corregir", new() { ["CategoriaId"] = categoria.ToString() });

    private Task<Caso> CasoAsync(Guid id) => fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));

    private Task<List<EventoCaso>> EventosAsync(Guid id, TipoEventoCaso tipo) =>
        fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking().Where(e => e.CasoId == id && e.Tipo == tipo).ToListAsync());

    private Task<List<CorreccionCategoria>> CorreccionesAsync(Guid id) =>
        fabrica.ConBaseDatosAsync(db => db.CorreccionesCategoria.AsNoTracking().Where(c => c.CasoId == id).OrderBy(c => c.CorregidaEn).ToListAsync());

    // ----- CA01 y CA02 -----

    [Fact]
    public async Task CA01_IniciarLaRevision_PasaAEnRevisionConResponsableFechaYHora()
    {
        var id = await ReporteAsync();
        var (operador, cuenta) = await OperadorAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await IniciarAsync(operador, id);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EstadoCaso.EnRevision, (await CasoAsync(id)).Estado);
        var evento = Assert.Single(await EventosAsync(id, TipoEventoCaso.RevisionIniciada));
        Assert.Equal(cuenta.Id, evento.ActorId);
        Assert.InRange(evento.OcurridoEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(VisibilidadEvento.Publica, evento.Visibilidad); // DP05: «En revisión» es un estado público
        var html = await operador.GetStringAsync(Ruta(id));
        Assert.Contains("Revisión iniciada.", html);
        Assert.Contains("Prueba Operador", html);
    }

    [Fact]
    public async Task CA02_UnReporteQueNoEstaRecibido_NoPuedeIniciarSuRevision()
    {
        var id = await ReporteAsync();
        var (primero, cuentaPrimero) = await OperadorAsync();
        var (segundo, _) = await OperadorAsync();
        await IniciarAsync(primero, id);

        var respuesta = await IniciarAsync(segundo, id);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode); // la página vuelve con el motivo
        Assert.Contains("La revisión ya la inició", System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        Assert.Equal(cuentaPrimero.Id, Assert.Single(await EventosAsync(id, TipoEventoCaso.RevisionIniciada)).ActorId);
    }

    [Fact]
    public async Task CA02_UnReporteYaAvanzado_NoVuelveAEnRevision()
    {
        var id = await ReporteAsync();
        // Simula un reporte que ya salió de la revisión (HU15–HU17) sin depender de esas historias.
        await fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlAsync($"UPDATE \"Casos\" SET \"Estado\" = 'Pendiente' WHERE \"Id\" = {id}"));
        var resultado = await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(RevisarReporte.MotivoNoRecibido, Assert.IsType<ResultadoRevision.Rechazado>(resultado).Motivo);
        Assert.Empty(await EventosAsync(id, TipoEventoCaso.RevisionIniciada));
    }

    [Fact]
    public async Task CA01_ElMismoOperadorQueReenvia_NoRegistraOtroCambio()
    {
        var id = await ReporteAsync();
        var (operador, _) = await OperadorAsync();

        await IniciarAsync(operador, id);
        var reenvio = await IniciarAsync(operador, id);

        Assert.Equal(HttpStatusCode.Redirect, reenvio.StatusCode);
        Assert.Single(await EventosAsync(id, TipoEventoCaso.RevisionIniciada));
    }

    [Fact]
    public async Task UnSos_NoSeRevisaComoReporte()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        var (operador, _) = await OperadorAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await operador.GetAsync(Ruta(sos))).StatusCode);
        var resultado = await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(sos, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<ResultadoRevision.NoEncontrado>(resultado);
        Assert.Equal(EstadoCaso.Pendiente, (await CasoAsync(sos)).Estado);
    }

    // ----- CA03 y CA04 -----

    [Fact]
    public async Task CA03_LaCorreccion_ConservaLaOriginalLaNuevaYElResponsableConFechaYHora()
    {
        var id = await ReporteAsync(Robo);
        var (operador, cuenta) = await OperadorAsync();
        await IniciarAsync(operador, id);

        var respuesta = await CorregirAsync(operador, id, Otros);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var caso = await CasoAsync(id);
        Assert.Equal(Otros, caso.CategoriaId);
        Assert.Equal(cuenta.Id, caso.ClasificadoPor);
        var correccion = Assert.Single(await CorreccionesAsync(id));
        Assert.Equal((Robo, Otros, cuenta.Id), (correccion.CategoriaAnteriorId, correccion.CategoriaNuevaId, correccion.OperadorId));
        Assert.Equal(Robo, (await fabrica.ConBaseDatosAsync(db => db.DetallesReporte.AsNoTracking().SingleAsync(d => d.CasoId == id))).CategoriaOriginalId);
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(id)));
        Assert.Contains("data-categoria-original>Robo o hurto<", html);
        Assert.Contains("data-categoria-vigente>Otros<", html);
        Assert.Contains("«Robo o hurto» → «Otros»", html);
    }

    [Fact]
    public async Task CA03_VariasCorrecciones_QuedanTodasRegistradasYLaOriginalNoCambia()
    {
        var id = await ReporteAsync(Robo);
        var (operador, _) = await OperadorAsync();
        await IniciarAsync(operador, id);

        await CorregirAsync(operador, id, Otros);
        await CorregirAsync(operador, id, ViolenciaDomestica);

        var correcciones = await CorreccionesAsync(id);
        Assert.Equal([(Robo, Otros), (Otros, ViolenciaDomestica)], correcciones.Select(c => (c.CategoriaAnteriorId, c.CategoriaNuevaId)));
        Assert.Equal(ViolenciaDomestica, (await CasoAsync(id)).CategoriaId);
        Assert.Equal(Robo, (await fabrica.ConBaseDatosAsync(db => db.DetallesReporte.AsNoTracking().SingleAsync(d => d.CasoId == id))).CategoriaOriginalId);
    }

    [Fact]
    public async Task CA03_LaMismaCategoria_NoRegistraUnaCorreccion()
    {
        var id = await ReporteAsync(Robo);
        var (operador, _) = await OperadorAsync();
        await IniciarAsync(operador, id);

        var respuesta = await CorregirAsync(operador, id, Robo);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Empty(await CorreccionesAsync(id));
    }

    [Fact]
    public async Task CA04_FueraDeLaRevision_NoSePermiteCorregir()
    {
        var id = await ReporteAsync(Robo);
        var (operador, _) = await OperadorAsync();

        var respuesta = await CorregirAsync(operador, id, Otros); // aún «Recibido»

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains(RevisarReporte.MotivoNoEnRevision, System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        Assert.Equal(Robo, (await CasoAsync(id)).CategoriaId);
        Assert.Empty(await CorreccionesAsync(id));
    }

    [Fact]
    public async Task CA04_DP07_UnaCategoriaFueraDelCatalogo_NoSeAcepta()
    {
        var id = await ReporteAsync(Robo);
        var (operador, _) = await OperadorAsync();
        await IniciarAsync(operador, id);

        var respuesta = await CorregirAsync(operador, id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains(RevisarReporte.MotivoCategoriaInvalida, await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await CorreccionesAsync(id));
    }

    [Fact]
    public async Task CA03_LaBaseDeDatos_RechazaUnaCorreccionSinCambio()
    {
        var id = await ReporteAsync(Robo);
        var operador = await fabrica.CrearAsync(Roles.Operador);

        await Assert.ThrowsAsync<DbUpdateException>(() => fabrica.ConBaseDatosAsync(async db =>
        {
            db.CorreccionesCategoria.Add(CorreccionCategoria.Registrar(id, Robo, Robo, operador.Id, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync();
        }));
    }

    // ----- CA05: revisión simultánea -----

    // Simula que otro operador inicia la revisión (o corrige) entre la lectura y el guardado de este operador.
    private sealed class OtroOperador(string conexion, Func<RevisarReporte, Task> accion, Cad.Server.Compartido.INotificador notificador) : SaveChangesInterceptor
    {
        private bool hecho;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData datos,
            InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (!hecho)
            {
                hecho = true;
                await using var otro = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion).Options);
                await accion(new RevisarReporte(otro, TimeProvider.System, notificador, NullLogger<RevisarReporte>.Instance));
            }
            return resultado;
        }
    }

    private async Task<ResultadoRevision> ConOtroOperadorAsync(Func<RevisarReporte, Task> otro, Func<RevisarReporte, Task<ResultadoRevision>> propio)
    {
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        await using var db = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion)
            .AddInterceptors(new OtroOperador(conexion, otro, fabrica.Services.GetRequiredService<Cad.Server.Compartido.INotificador>())).Options);
        return await propio(new RevisarReporte(db, TimeProvider.System, fabrica.Services.GetRequiredService<Cad.Server.Compartido.INotificador>(), NullLogger<RevisarReporte>.Instance));
    }

    [Fact]
    public async Task CA05_DosOperadoresALaVez_RegistranUnSoloCambioConUnResponsable()
    {
        var id = await ReporteAsync();
        var a = await fabrica.CrearAsync(Roles.Operador);
        var b = await fabrica.CrearAsync(Roles.Operador);

        var resultado = await ConOtroOperadorAsync(
            otro => otro.IniciarAsync(id, b.Id, CancellationToken.None),
            propio => propio.IniciarAsync(id, a.Id, CancellationToken.None));

        Assert.Contains("ya la inició", Assert.IsType<ResultadoRevision.Rechazado>(resultado).Motivo);
        Assert.Equal(b.Id, Assert.Single(await EventosAsync(id, TipoEventoCaso.RevisionIniciada)).ActorId);
        Assert.Equal(EstadoCaso.EnRevision, (await CasoAsync(id)).Estado);
    }

    [Fact]
    public async Task CA05_DiezOperadoresALaVez_SoloUnoQuedaComoResponsable()
    {
        var id = await ReporteAsync();
        var operadores = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fabrica.CrearAsync(Roles.Operador)));

        var resultados = await Task.WhenAll(operadores.Select(o =>
            fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, o.Id, CancellationToken.None))));

        Assert.Single(resultados, r => r is ResultadoRevision.Registrado);
        Assert.Single(await EventosAsync(id, TipoEventoCaso.RevisionIniciada));
    }

    [Fact]
    public async Task CA03_CA05_DosCorreccionesALaVez_QuedanAmbasEnOrdenSinPerderLaAnterior()
    {
        var id = await ReporteAsync(Robo);
        var a = await fabrica.CrearAsync(Roles.Operador);
        var b = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, a.Id, CancellationToken.None));

        var resultado = await ConOtroOperadorAsync(
            otro => otro.CorregirCategoriaAsync(id, Otros, b.Id, CancellationToken.None),
            propio => propio.CorregirCategoriaAsync(id, ViolenciaDomestica, a.Id, CancellationToken.None));

        Assert.IsType<ResultadoRevision.Registrado>(resultado);
        var correcciones = await CorreccionesAsync(id);
        // La segunda parte de la categoría que dejó la primera (nueva evaluación tras el conflicto).
        Assert.Equal([(Robo, Otros, b.Id), (Otros, ViolenciaDomestica, a.Id)],
            correcciones.Select(c => (c.CategoriaAnteriorId, c.CategoriaNuevaId, c.OperadorId)));
        Assert.Equal(ViolenciaDomestica, (await CasoAsync(id)).CategoriaId);
    }

    // ----- Página «Reportes» (E5) -----

    [Fact]
    public async Task E5_LaPaginaReportes_MuestraRecibidosYEnRevision_SinSos()
    {
        var recibido = await ReporteAsync();
        var enRevision = await ReporteAsync();
        var (operador, _) = await OperadorAsync();
        await IniciarAsync(operador, enRevision);
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!.Id;

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/reportes"));

        Assert.Contains($"data-reporte=\"{recibido}\"", html);
        Assert.Contains($"data-reporte=\"{enRevision}\"", html);
        Assert.DoesNotContain(sos.ToString(), html);
        Assert.Contains("En revisión", html);
        Assert.Contains("href=\"/panel/reportes\"", html); // enlace en la navegación del operador
    }

    [Fact]
    public async Task E5_ElDetalle_MuestraElHechoYLaUbicacionDelHecho()
    {
        var id = await ReporteAsync(descripcion: "Línea uno\nLínea dos (ficticio)");
        var (operador, _) = await OperadorAsync();

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync(Ruta(id)));

        Assert.Contains("Línea uno\nLínea dos (ficticio)", html);
        Assert.Contains("Punto elegido en el mapa por el ciudadano", html);
        Assert.Contains("Frente al mercado", html);
        Assert.Contains("id=\"mapa-hecho\"", html);
        Assert.Contains("Iniciar la revisión", html);
        Assert.DoesNotContain("Corregir la categoría", html); // CA04: solo en revisión
    }

    [Fact]
    public async Task HU03_UnAdministrador_NoRevisaNiCorrige()
    {
        var id = await ReporteAsync(Robo);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);

        // Con el token de su propia sesión (de una página que sí puede abrir), la acción se rechaza en el servidor.
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/admin/cuentas");
        var respuesta = await navegador.PostAsync($"{Ruta(id)}?handler=IniciarRevision",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EstadoCaso.Recibido, (await CasoAsync(id)).Estado);
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Sereno)]
    public async Task HU03_LaAppDeOtrosPerfiles_NoObtieneLosReportes(string rol)
    {
        var id = await ReporteAsync();
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        foreach (var ruta in new[] { "/panel/reportes", Ruta(id) })
        {
            var respuesta = await app.GetAsync(ruta);
            Assert.False(respuesta.IsSuccessStatusCode, $"{ruta}: {(int)respuesta.StatusCode}");
        }
    }

    // ----- Historial (HU27) y registros (RNF07) -----

    [Fact]
    public async Task HU27_ElHistorial_MuestraLaRevisionYLasCorrecciones()
    {
        var id = await ReporteAsync(Robo);
        var (operador, _) = await OperadorAsync();
        await IniciarAsync(operador, id);
        await CorregirAsync(operador, id, Otros);

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos/{id}/historial"));

        Assert.Contains("Reporte registrado como «Robo o hurto»", html);
        Assert.Contains("Revisión iniciada", html);
        Assert.Contains("Categoría corregida de «Robo o hurto» a «Otros»", html);
        Assert.DoesNotContain("Clasificado como", html);
        Assert.Contains("data-origen-estado=\"Reporte En revisión\"", html);
    }

    [Fact]
    public async Task RNF07_LaRevision_NoRegistraLaDescripcionEnLosRegistros()
    {
        var id = await ReporteAsync(Robo, "Descripción ficticia única para la revisión");
        var (operador, _) = await OperadorAsync();

        await IniciarAsync(operador, id);
        await CorregirAsync(operador, id, Otros);
        await operador.GetStringAsync(Ruta(id));

        Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains("Descripción ficticia única para la revisión"));
    }
}
