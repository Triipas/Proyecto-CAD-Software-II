using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Consultas;

// HU26 — Buscar y filtrar casos (S3-T03, S3-T04). Datos ficticios. La base de pruebas es compartida: cada prueba se
// aísla con su propio sereno o con una fecha antigua exclusiva.
[Collection(ColeccionServidor.Nombre)]
public partial class BusquedaCasosTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static readonly Guid Otros = CategoriaIncidencia.Iniciales[3].Id;

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private async Task<Guid> SosAsync(bool conUbicacion = true)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var ahora = DateTimeOffset.UtcNow;
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora,
            conUbicacion ? new UbicacionSos(-12.05, -77.03, 8, ahora.AddSeconds(1)) : null));
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
    }

    // Un reporte admitido (HU10, HU14, HU15).
    private async Task<Guid> ReporteAdmitidoAsync(Guid categoria, bool conPunto = true)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var lugar = conPunto
            ? new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.06, -77.04, null, null)
            : new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Pasaje ficticio");
        var id = (await (await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), categoria, "Hecho ficticio",
            DateTimeOffset.UtcNow.AddHours(-1), lugar))).Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None));
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<DecidirReporte>().EjecutarAsync(id,
            new DatosDecision(TipoDecisionReporte.Admision, "Motivo ficticio", Prioridad: "Media", Heridos: "No"), operador.Id, CancellationToken.None));
        return id;
    }

    private Task Clasificar(Guid casoId, Guid categoria) => fabrica.ConServiciosAsync(async s =>
    {
        var operador = await fabrica.CrearAsync(Roles.Operador);
        return await s.GetRequiredService<ClasificarCaso>().EjecutarAsync(casoId, categoria, "Media", "No", operador.Id, CancellationToken.None);
    });

    // Un sereno operativo; cada asignación lo ocupa, así que se libera cerrando la asignación anterior.
    private async Task<CuentaDePrueba> SerenoAsync()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        return sereno;
    }

    private Task<ResultadoAsignacion> Asignar(Guid casoId, Guid serenoId) => fabrica.ConServiciosAsync(async s =>
    {
        var operador = await fabrica.CrearAsync(Roles.Operador);
        return await s.GetRequiredService<AsignarCaso>().EjecutarAsync(casoId, serenoId, operador.Id, Guid.NewGuid(), CancellationToken.None);
    });

    // Cierra el caso (en camino, atención y cierre por el sereno) para que el sereno quede libre para otra asignación.
    private Task Cerrar(Guid casoId, Guid serenoId, Guid? tipificacion = null) => fabrica.ConServiciosAsync(async s =>
    {
        var avanzar = s.GetRequiredService<AvanzarCaso>();
        await avanzar.EjecutarAsync(casoId, Avance.ConfirmarDesplazamiento, serenoId, ActorAvance.Sereno, CancellationToken.None);
        await avanzar.EjecutarAsync(casoId, Avance.IniciarAtencion, serenoId, ActorAvance.Sereno, CancellationToken.None, await fabrica.EnElLugarAsync(casoId));
        return await s.GetRequiredService<CerrarCaso>().EjecutarAsync(casoId, new DatosCierre(tipificacion ?? Robo, "ResueltoEnElLugar", "Resultado ficticio", null), serenoId,
            ActorAvance.Sereno, CancellationToken.None);
    });

    private static List<Guid> Ids(string html) => CasoRegex().Matches(html).Select(m => Guid.Parse(m.Groups[1].Value)).ToList();

    [GeneratedRegex("data-caso=\"([0-9a-f-]{36})\"")]
    private static partial Regex CasoRegex();

    // ----- CA01 -----

    [Fact]
    public async Task CA01_LosFiltros_SeCombinanYCadaResultadoLosCumpleTodos()
    {
        var sereno = await SerenoAsync();
        var sosRobo = await SosAsync();
        await Clasificar(sosRobo, Robo);
        Assert.IsType<ResultadoAsignacion.Asignado>(await Asignar(sosRobo, sereno.Id));
        await Cerrar(sosRobo, sereno.Id);
        var sosSinTipo = await SosAsync();
        Assert.IsType<ResultadoAsignacion.Asignado>(await Asignar(sosSinTipo, sereno.Id));
        await Cerrar(sosSinTipo, sereno.Id, Otros); // cerrado: cuenta su tipificación final
        var reporteRobo = await ReporteAdmitidoAsync(Robo);
        Assert.IsType<ResultadoAsignacion.Asignado>(await Asignar(reporteRobo, sereno.Id)); // queda «Asignada»
        var operador = await OperadorAsync();

        Assert.Equal([reporteRobo, sosSinTipo, sosRobo], Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}")));
        Assert.Equal([sosSinTipo, sosRobo], Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&origen=Sos")));
        Assert.Equal([sosRobo], Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&origen=Sos&tipo={Robo}")));
        Assert.Equal([reporteRobo], Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&tipo={Robo}&estado=Asignada")));
        Assert.Empty(Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&origen=Reporte&estado=Cerrada")));
        var hoy = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")).ToString("yyyy-MM-dd");
        Assert.Equal(3, Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&desde={hoy}&hasta={hoy}")).Count);
        // CA05: un reporte con punto muestra el lugar del hecho; un SOS con ubicación, la del dispositivo.
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}"));
        Assert.Contains("<td data-ubicacion>Lugar del hecho</td>", Regex.Match(html, $"<tr data-caso=\"{reporteRobo}\">.*?</tr>", RegexOptions.Singleline).Value);
        Assert.Contains("<td data-ubicacion>Del dispositivo</td>", Regex.Match(html, $"<tr data-caso=\"{sosRobo}\">.*?</tr>", RegexOptions.Singleline).Value);
    }

    [Fact]
    public async Task CA01_DP17_LasFechas_SeAplicanALaFechaDeRegistroPorDiasCompletos()
    {
        var sereno = await SerenoAsync();
        var sos = await SosAsync();
        await Asignar(sos, sereno.Id);
        var operador = await OperadorAsync();
        var lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, lima).DateTime);

        Assert.Single(Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&desde={hoy:yyyy-MM-dd}")));
        Assert.Single(Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&hasta={hoy:yyyy-MM-dd}")));
        Assert.Empty(Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&hasta={hoy.AddDays(-1):yyyy-MM-dd}")));
        Assert.Empty(Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&desde={hoy.AddDays(1):yyyy-MM-dd}")));
    }

    [Fact]
    public async Task DP17_SinFiltros_LosMasRecientesPrimero_EnPaginasDe50()
    {
        // 51 casos en un día antiguo exclusivo de esta prueba.
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var dia = new DateTimeOffset(2001, 2, 3, 15, 0, 0, TimeSpan.Zero);
        var ids = await fabrica.ConBaseDatosAsync(async db =>
        {
            var casos = Enumerable.Range(0, 51).Select(i => Caso.EmitirSos(ciudadano.Id, Guid.NewGuid(), null, dia.AddMinutes(i))).ToList();
            db.Casos.AddRange(casos);
            await db.SaveChangesAsync();
            return casos.Select(c => c.Id).ToList();
        });
        var operador = await OperadorAsync();

        var primera = await operador.GetStringAsync("/panel/casos?desde=2001-02-03&hasta=2001-02-03");
        var segunda = await operador.GetStringAsync("/panel/casos?desde=2001-02-03&hasta=2001-02-03&pagina=2");

        Assert.Equal(ids.AsEnumerable().Reverse().Take(50), Ids(primera));
        Assert.Equal([ids[0]], Ids(segunda));
        Assert.Contains("51 casos con los filtros aplicados · página 1 de 2", primera);
        Assert.Contains("href=\"/panel/casos?desde=2001-02-03&amp;hasta=2001-02-03&amp;pagina=2\"", primera);
    }

    // ----- CA02 -----

    [Fact]
    public async Task CA02_UnSosSinClasificar_ApareceComoTipoPorDeterminar_YSePuedeFiltrar()
    {
        var sereno = await SerenoAsync();
        var otroSereno = await SerenoAsync();
        var sinTipo = await SosAsync();
        await Asignar(sinTipo, sereno.Id); // en curso y sin clasificar (un caso cerrado ya tiene su tipificación final)
        var clasificado = await SosAsync();
        await Clasificar(clasificado, Otros);
        await Asignar(clasificado, otroSereno.Id);
        var operador = await OperadorAsync();

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}"));
        var filaSinTipo = Regex.Match(html, $"<tr data-caso=\"{sinTipo}\">.*?</tr>", RegexOptions.Singleline).Value;

        Assert.Contains("Tipo por determinar", filaSinTipo);
        Assert.Equal([sinTipo], Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&tipo=sin-tipo")));
    }

    // Decisión del PO (24/10/2026): en un caso cerrado, el tipo es la tipificación final, no la clasificación inicial.
    [Fact]
    public async Task UnCasoCerrado_SeBuscaYSeMuestraPorSuTipificacionFinal()
    {
        var sereno = await SerenoAsync();
        var cerrado = await SosAsync();
        await Clasificar(cerrado, Robo);
        Assert.IsType<ResultadoAsignacion.Asignado>(await Asignar(cerrado, sereno.Id));
        await Cerrar(cerrado, sereno.Id, Otros);
        var enCurso = await SosAsync();
        await Clasificar(enCurso, Robo);
        Assert.IsType<ResultadoAsignacion.Asignado>(await Asignar(enCurso, sereno.Id));
        var operador = await OperadorAsync();

        Assert.Equal([enCurso], Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&tipo={Robo}")));
        Assert.Equal([cerrado], Ids(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}&tipo={Otros}")));
        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}"));
        Assert.Contains($"<td data-tipo>{CategoriaIncidencia.Iniciales[3].Nombre}</td>", Regex.Match(html, $"<tr data-caso=\"{cerrado}\">.*?</tr>", RegexOptions.Singleline).Value);
        Assert.Contains($"<td data-tipo>{CategoriaIncidencia.Iniciales[1].Nombre}</td>", Regex.Match(html, $"<tr data-caso=\"{enCurso}\">.*?</tr>", RegexOptions.Singleline).Value);
        Assert.Contains("En los casos cerrados, la tipificación final.", html);
    }

    // 24/10/2026: un reporte admitido que espera sereno se muestra «Admitido» en todo el panel (búsqueda, detalle, cola),
    // como en sus pasos de progreso y en la app del ciudadano; «Pendiente» queda para el SOS.
    [Fact]
    public async Task UnReporteAdmitido_SeMuestraAdmitido_EnLaBusquedaElDetalleYLaCola()
    {
        var reporte = await ReporteAdmitidoAsync(Robo);
        var sos = await SosAsync();
        var operador = await OperadorAsync();

        // Base compartida: se acota al día y al tipo para que los dos casos estén en la primera página.
        var hoy = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")).ToString("yyyy-MM-dd");
        await Clasificar(sos, Robo);
        var busqueda = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos?estado=Pendiente&tipo={Robo}&desde={hoy}&hasta={hoy}"));
        var filaReporte = Regex.Match(busqueda, $"<tr data-caso=\"{reporte}\">.*?</tr>", RegexOptions.Singleline).Value;
        var filaSos = Regex.Match(busqueda, $"<tr data-caso=\"{sos}\">.*?</tr>", RegexOptions.Singleline).Value;
        Assert.Contains("Admitido</span>", filaReporte);
        Assert.Contains("Pendiente</span>", filaSos);
        Assert.Contains("Pendiente (SOS) / Admitido (reporte)", busqueda);

        var detalle = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/reportes/{reporte}"));
        Assert.Matches(new Regex("<span data-estado>\\s*<span class=\"chip chip--advertencia\">.*?Admitido</span>", RegexOptions.Singleline), detalle);
        Assert.Contains("Asignada", detalle); // los pasos se llaman como los estados

        var cola = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/central"));
        Assert.Contains("<span class=\"sos__estado\">Admitido</span>", Regex.Match(cola, $"id=\"sos-{reporte}\".*?</li>", RegexOptions.Singleline).Value);
    }

    // ----- CA03 y CA04 -----

    [Fact]
    public async Task CA03_LimpiarFiltros_VuelveALaBusquedaSinFiltros()
    {
        var operador = await OperadorAsync();

        var conFiltros = await operador.GetStringAsync("/panel/casos?origen=Sos&estado=Pendiente");
        var sinFiltros = await operador.GetStringAsync("/panel/casos");

        Assert.Contains("<a class=\"boton boton--secundario\" href=\"/panel/casos\" data-limpiar>Limpiar filtros</a>", conFiltros);
        Assert.Contains("<option value=\"Sos\" selected=\"selected\">", conFiltros);
        Assert.DoesNotContain("selected=\"selected\"", sinFiltros);
        Assert.DoesNotContain("con los filtros aplicados", sinFiltros);
    }

    [Fact]
    public async Task CA04_SinCoincidencias_SeInforma()
    {
        var sereno = await SerenoAsync(); // sin casos
        var operador = await OperadorAsync();

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}"));

        Assert.Contains("data-sin-coincidencias", html);
        Assert.Contains("No existen casos que coincidan con los filtros", html);
        Assert.DoesNotContain("data-caso=", html);
    }

    [Theory]
    [InlineData("estado=Inventado", "Elige un estado de la lista.")]
    [InlineData("origen=Otro", "Elige un origen de la lista.")]
    [InlineData("desde=31-12-2026", "Escribe una fecha válida.")]
    [InlineData("desde=2026-10-15&hasta=2026-10-14", "La fecha final es anterior a la inicial.")]
    public async Task UnFiltroNoValido_SeExplicaYNoSeBusca(string consulta, string mensaje)
    {
        var operador = await OperadorAsync();

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos?{consulta}"));

        Assert.Contains(mensaje, html);
        Assert.DoesNotContain("data-casos", html);
    }

    // ----- CA05 -----

    [Fact]
    public async Task CA05_UnCasoSinUbicacion_ApareceSinCoordenadas()
    {
        var sereno = await SerenoAsync();
        var sos = await SosAsync(conUbicacion: false);
        await Asignar(sos, sereno.Id);
        await Cerrar(sos, sereno.Id);
        var reporte = await ReporteAdmitidoAsync(Otros, conPunto: false);
        await Asignar(reporte, sereno.Id);
        var operador = await OperadorAsync();

        var html = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync($"/panel/casos?sereno={sereno.Id}"));

        Assert.Equal(2, Regex.Matches(html, "<td data-ubicacion>Sin ubicación</td>").Count);
        Assert.DoesNotContain("-12.", html);
    }

    // ----- CA06 -----

    [Fact]
    public async Task CA06_UnAdministrador_NoConsultaLosCasos()
    {
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);

        var respuesta = await navegador.GetAsync("/panel/casos?origen=Sos");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Sereno)]
    public async Task CA06_LaAppDeOtrosPerfiles_NoConsultaLosCasos(string rol)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));

        var respuesta = await app.GetAsync("/panel/casos");

        Assert.False(respuesta.IsSuccessStatusCode, $"{(int)respuesta.StatusCode}");
    }

    [Fact]
    public async Task LaNavegacionDelOperador_EnlazaLaBusqueda()
    {
        var operador = await OperadorAsync();

        Assert.Contains("href=\"/panel/casos\"", await operador.GetStringAsync("/panel/central"));
    }
}
