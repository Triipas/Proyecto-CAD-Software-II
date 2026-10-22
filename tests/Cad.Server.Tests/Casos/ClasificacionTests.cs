using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Casos;

// HU13 — Clasificar un SOS después de su registro (S2-T08). Catálogo compartido (DP07) y solo el operador
// clasifica (DP09). Reclasificar no está definido en la HU: se rechaza (decisión pendiente). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class ClasificacionTests(FabricaCad fabrica)
{
    private static readonly Guid Violencia = CategoriaIncidencia.Iniciales[0].Id;
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;

    private static string Detalle(Guid casoId) => $"/panel/central/sos/{casoId}";

    private async Task<Guid> SosAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano, telefono: "987654321"));
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(-12.0464, -77.0428, 10, ahora.AddSeconds(1))));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    private async Task<CuentaDePrueba> SerenoOperativoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return cuenta;
    }

    private async Task<(HttpClient Navegador, CuentaDePrueba Cuenta)> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, cuenta);
    }

    private static async Task<HttpResponseMessage> ClasificarAsync(HttpClient navegador, Guid casoId, string categoria, string? paginaToken = null)
    {
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, paginaToken ?? Detalle(casoId));
        return await navegador.PostAsync($"{Detalle(casoId)}?handler=Clasificar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Clasificacion.CategoriaId"] = categoria,
            ["__RequestVerificationToken"] = token,
        }));
    }

    private static async Task<HttpResponseMessage> AsignarAsync(HttpClient navegador, Guid casoId, Guid serenoId, string? token = null)
    {
        token ??= await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, Detalle(casoId));
        return await navegador.PostAsync($"{Detalle(casoId)}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = serenoId.ToString(),
            ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = token,
        }));
    }

    private Task<Caso> CasoAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == casoId));

    private Task<int> EventosClasificacionAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.CasoId == casoId && e.Tipo == TipoEventoCaso.CasoClasificado));

    // Texto del elemento del SOS en el fragmento de la lista de la central.
    private static string ElementoDeLista(string lista, Guid casoId)
    {
        var inicio = lista.IndexOf($"data-sos=\"{casoId}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, "El SOS no está en la lista de la central.");
        return lista[inicio..lista.IndexOf("</li>", inicio, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task CA01_SosSinTipo_SeClasificaConResponsableFechaYHora_YActualizaLaCentral()
    {
        var casoId = await SosAsync();
        var (operador, cuentaOperador) = await PanelAsync();
        await using var central = await fabrica.OperadorEnCentralAsync();
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await ClasificarAsync(operador, casoId, Robo.ToString());

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var caso = await CasoAsync(casoId);
        Assert.Equal(Robo, caso.CategoriaId);
        Assert.Equal(cuentaOperador.Id, caso.ClasificadoPor);
        Assert.InRange(caso.ClasificadoEn!.Value, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(EstadoCaso.Pendiente, caso.Estado); // clasificar no cambia el estado
        Assert.Equal(1, await EventosClasificacionAsync(casoId));

        var detalle = await operador.GetStringAsync(Detalle(casoId)); // primero: el mensaje se muestra una sola vez
        Assert.Contains("SOS clasificado.", detalle);
        Assert.Contains("<dd data-tipo>Robo o hurto</dd>", detalle);
        Assert.Contains("<dd>Prueba Operador</dd>", detalle);
        Assert.DoesNotContain("Clasificar el SOS", detalle); // ya no se ofrece el formulario
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.AvisosCasos.Contains(casoId)), "No llegó el aviso del caso.");
        Assert.Contains("Tipo: Robo", ElementoDeLista(await operador.GetStringAsync("/panel/central?handler=Lista"), casoId));
    }

    [Fact]
    public async Task CA02_CA03_SinClasificar_SePresentaComoTipoPorDeterminarYSePuedeAsignar()
    {
        var casoId = await SosAsync(); // CA02: la emisión no pide tipo
        var sereno = await SerenoOperativoAsync();
        var (operador, _) = await PanelAsync();

        Assert.Contains("data-tipo>Tipo por determinar</p>", ElementoDeLista(await operador.GetStringAsync("/panel/central?handler=Lista"), casoId));
        Assert.Contains("Tipo por determinar", await operador.GetStringAsync(Detalle(casoId)));

        var respuesta = await AsignarAsync(operador, casoId, sereno.Id);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var caso = await CasoAsync(casoId);
        Assert.Equal(EstadoCaso.Asignada, caso.Estado);
        Assert.Null(caso.CategoriaId);
    }

    [Fact]
    public async Task UnCasoYaAsignado_SePuedeClasificar_YLaAsignacionSigueVigente()
    {
        var casoId = await SosAsync();
        var sereno = await SerenoOperativoAsync();
        var (operador, _) = await PanelAsync();
        await AsignarAsync(operador, casoId, sereno.Id);

        var respuesta = await ClasificarAsync(operador, casoId, Violencia.ToString());

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var caso = await CasoAsync(casoId);
        Assert.Equal(Violencia, caso.CategoriaId);
        Assert.Equal(EstadoCaso.Asignada, caso.Estado);
        Assert.True(await fabrica.ConBaseDatosAsync(db => db.Asignaciones.AnyAsync(a => a.CasoId == casoId && a.Vigente)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-una-categoria")]
    [InlineData("11111111-2222-3333-4444-555555555555")] // identificador fuera del catálogo
    public async Task CA04_CategoriaFueraDelCatalogo_SeRechazaSinModificarElCaso(string categoria)
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        var respuesta = await ClasificarAsync(operador, casoId, categoria);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Elige una categoría del catálogo.", await respuesta.Content.ReadAsStringAsync());
        var caso = await CasoAsync(casoId);
        Assert.Null(caso.CategoriaId);
        Assert.Null(caso.ClasificadoPor);
        Assert.Equal(0, await EventosClasificacionAsync(casoId));
    }

    [Fact]
    public async Task CA04_DP07_ElFormularioOfreceSoloLasCategoriasDelCatalogo()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        var detalle = await operador.GetStringAsync(Detalle(casoId));

        var inicio = detalle.IndexOf("id=\"Clasificacion_CategoriaId\"", StringComparison.Ordinal);
        var select = detalle[inicio..detalle.IndexOf("</select>", inicio, StringComparison.Ordinal)];
        foreach (var categoria in CategoriaIncidencia.Iniciales)
        {
            Assert.Contains($"value=\"{categoria.Id}\"", select);
            Assert.Contains($">{categoria.Nombre}</option>", select);
        }
        Assert.Equal(CategoriaIncidencia.Iniciales.Count + 1, select.Split("<option").Length - 1); // + «Elige una categoría»
    }

    [Fact]
    public async Task Reclasificar_SeRechaza_YConservaLaPrimeraClasificacion()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();
        await ClasificarAsync(operador, casoId, Robo.ToString());

        var respuesta = await ClasificarAsync(operador, casoId, Violencia.ToString());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("El SOS ya está clasificado como «Robo o hurto»", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(Robo, (await CasoAsync(casoId)).CategoriaId);
        Assert.Equal(1, await EventosClasificacionAsync(casoId));
    }

    [Fact]
    public async Task LaMismaClasificacionReenviada_NoRegistraOtroEvento()
    {
        var casoId = await SosAsync();
        var (operador, _) = await PanelAsync();

        var primera = await ClasificarAsync(operador, casoId, Robo.ToString());
        var reenvio = await ClasificarAsync(operador, casoId, Robo.ToString());

        Assert.Equal(HttpStatusCode.Redirect, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, reenvio.StatusCode);
        Assert.Equal(1, await EventosClasificacionAsync(casoId));
    }

    [Fact]
    public async Task ClasificacionesSimultaneasConCategoriasDistintas_SoloUnaSeConfirma()
    {
        var casoId = await SosAsync();
        var operadores = new List<HttpClient>();
        foreach (var _ in CategoriaIncidencia.Iniciales)
            operadores.Add((await PanelAsync()).Navegador);
        var tokens = await Task.WhenAll(operadores.Select(o => CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(o, Detalle(casoId))));

        var respuestas = await Task.WhenAll(operadores.Select((o, i) => o.PostAsync($"{Detalle(casoId)}?handler=Clasificar",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Clasificacion.CategoriaId"] = CategoriaIncidencia.Iniciales[i].Id.ToString(),
                ["__RequestVerificationToken"] = tokens[i],
            }))));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Redirect));
        Assert.Equal(1, await EventosClasificacionAsync(casoId));
        Assert.NotNull((await CasoAsync(casoId)).CategoriaId);
    }

    // Clasificar y asignar cambian el mismo caso (token xmin): ninguna de las dos debe perderse ni rechazarse
    // como «ya no está pendiente» por un conflicto que no afecta a su decisión.
    [Fact]
    public async Task ClasificarYAsignarALaVez_AmbasSeConfirman()
    {
        var (clasificador, _) = await PanelAsync();
        var (asignador, _) = await PanelAsync();
        for (var i = 0; i < 5; i++)
        {
            var casoId = await SosAsync();
            var sereno = await SerenoOperativoAsync();
            var tokenClasificar = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(clasificador, Detalle(casoId));
            var tokenAsignar = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(asignador, Detalle(casoId));

            var respuestas = await Task.WhenAll(
                clasificador.PostAsync($"{Detalle(casoId)}?handler=Clasificar", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["Clasificacion.CategoriaId"] = Robo.ToString(), ["__RequestVerificationToken"] = tokenClasificar,
                })),
                AsignarAsync(asignador, casoId, sereno.Id, tokenAsignar));

            Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
            var caso = await CasoAsync(casoId);
            Assert.Equal(Robo, caso.CategoriaId);
            Assert.Equal(EstadoCaso.Asignada, caso.Estado);
        }
    }

    [Fact]
    public async Task DP09_UnAdministradorConTokenValido_NoPuedeClasificar()
    {
        var casoId = await SosAsync();
        var (administrador, _) = await PanelAsync(Roles.Administrador);

        var respuesta = await ClasificarAsync(administrador, casoId, Robo.ToString(), paginaToken: "/panel/admin/cuentas/nueva");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
        Assert.Null((await CasoAsync(casoId)).CategoriaId);
    }

    [Theory]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Ciudadano)]
    public async Task DP09_SerenoOCiudadanoConSuToken_NoPuedenClasificar(string rol)
    {
        var casoId = await SosAsync();
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var respuesta = await app.PostAsync($"{Detalle(casoId)}?handler=Clasificar",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Clasificacion.CategoriaId"] = Robo.ToString() }));

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Null((await CasoAsync(casoId)).CategoriaId);
    }

    [Fact]
    public void Dominio_UnCasoClasificado_NoSeVuelveAClasificar()
    {
        var caso = Caso.EmitirSos(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        caso.Clasificar(Robo, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => caso.Clasificar(Violencia, Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Equal(Robo, caso.CategoriaId);
    }

    [Fact]
    public async Task Persistencia_UnaClasificacionIncompleta_LaRechazaLaBaseDeDatos()
    {
        var casoId = await SosAsync();

        var error = await Record.ExceptionAsync(() => fabrica.ConBaseDatosAsync(db =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Casos\" SET \"CategoriaId\" = {Robo} WHERE \"Id\" = {casoId}")));

        Assert.NotNull(error); // CK_Casos_ClasificacionCompleta: sin responsable ni fecha no hay clasificación
        Assert.Null((await CasoAsync(casoId)).CategoriaId);
    }
}
