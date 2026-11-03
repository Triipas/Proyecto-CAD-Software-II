using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Modulos.Mediciones;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Consultas;

// HU28 — Consultar los tiempos de respuesta y de atención (S5-T10). DP18 (PO, 16/10/2026): por caso, en su detalle y como
// columnas de la búsqueda de «Casos»; sin promedios. Las marcas se fijan con SQL para que los tiempos sean exactos. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class TiemposCasoTests(FabricaCad fabrica)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    // ----- Cálculo (sin base de datos) -----

    [Theory]
    [InlineData(0, 0, 45, "45 s")]
    [InlineData(0, 3, 20, "3 min 20 s")]
    [InlineData(1, 2, 3, "1 h 02 min 03 s")]
    [InlineData(26, 0, 0, "26 h 00 min 00 s")]
    public void Duracion_SeMuestraEnHorasMinutosYSegundos(int horas, int minutos, int segundos, string esperado) =>
        Assert.Equal(esperado, TiemposCaso.Duracion(new TimeSpan(horas, minutos, segundos)));

    [Fact]
    public void CA01_Sos_RespuestaEsDelRegistroAlInicioDeAtencion()
    {
        var t = new TiemposCaso(OrigenCaso.Sos, T0, null, null, T0.AddMinutes(7).AddSeconds(5));
        Assert.Equal("7 min 05 s", t.Respuesta);
        Assert.Equal(t.Respuesta, t.HastaInicioAtencion);
        Assert.Equal(TiemposCaso.NoCorresponde, t.HastaDecision);
    }

    [Fact]
    public void CA02_Reporte_TiemposPorSeparado()
    {
        var t = new TiemposCaso(OrigenCaso.Reporte, T0, T0.AddMinutes(10), TipoDecisionReporte.Admision, T0.AddHours(1).AddMinutes(2).AddSeconds(3));
        Assert.Equal("10 min 00 s", t.HastaDecision);
        Assert.Equal("1 h 02 min 03 s", t.HastaAtencion);
        Assert.Equal(TiemposCaso.NoCorresponde, t.Respuesta);
    }

    [Theory]
    [InlineData(TipoDecisionReporte.Derivacion)]
    [InlineData(TipoDecisionReporte.Descarte)]
    public void CA02_ReporteDerivadoODescartado_LaAtencionNoCorresponde(TipoDecisionReporte decision)
    {
        var t = new TiemposCaso(OrigenCaso.Reporte, T0, T0.AddMinutes(4), decision, null);
        Assert.Equal("4 min 00 s", t.HastaDecision);
        Assert.Equal(TiemposCaso.NoCorresponde, t.HastaAtencion);
    }

    [Fact]
    public void CA03_SinMarca_NoDisponible()
    {
        Assert.Equal(TiemposCaso.NoDisponible, new TiemposCaso(OrigenCaso.Sos, T0, null, null, null).Respuesta);
        var reporte = new TiemposCaso(OrigenCaso.Reporte, T0, null, null, null);
        Assert.Equal((TiemposCaso.NoDisponible, TiemposCaso.NoDisponible), (reporte.HastaDecision, reporte.HastaAtencion));
        var admitidoSinAtencion = new TiemposCaso(OrigenCaso.Reporte, T0, T0.AddMinutes(1), TipoDecisionReporte.Admision, null);
        Assert.Equal(TiemposCaso.NoDisponible, admitidoSinAtencion.HastaAtencion);
        // Una marca anterior al registro sería incoherente: no se presenta un tiempo negativo.
        Assert.Equal(TiemposCaso.NoDisponible, new TiemposCaso(OrigenCaso.Sos, T0, null, null, T0.AddSeconds(-1)).Respuesta);
    }

    // ----- Consulta y presentación (servidor con PostgreSQL) -----

    private async Task<HttpClient> OperadorAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private async Task<(Guid Id, CuentaDePrueba Sereno)> SosAsync()
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var respuesta = await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null));
        return ((await respuesta.Content.ReadFromJsonAsync<RespuestaSos>())!.Id, await fabrica.CrearAsync(Roles.Sereno));
    }

    private async Task AtenderAsync(Guid id, CuentaDePrueba sereno)
    {
        var app = await fabrica.CrearClienteAppAsync(sereno);
        Assert.Equal(HttpStatusCode.OK, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoAsignacion.Asignado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<AsignarCaso>().EjecutarAsync(id, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None)));
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsync($"/api/sereno/casos/{id}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsync($"/api/sereno/casos/{id}/atencion", null)).StatusCode);
    }

    private async Task<Guid> ReporteDecididoAsync(TipoDecisionReporte decision)
    {
        var app = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var id = (await (await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), CategoriaIncidencia.Iniciales[1].Id,
            "Hecho ficticio", DateTimeOffset.UtcNow.AddHours(-1), new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Pasaje ficticio"))))
            .Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<RevisarReporte>().IniciarAsync(id, operador.Id, CancellationToken.None));
        var datos = decision switch
        {
            TipoDecisionReporte.Derivacion => new DatosDecision(decision, "Motivo ficticio", "Otro", "Se comunicó el caso", "Comisaría ficticia"),
            _ => new DatosDecision(decision, "Motivo ficticio", Prioridad: "Media", Heridos: "No", MotivoDescarte: "Duplicado"),
        };
        Assert.IsType<ResultadoDecision.Registrado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<DecidirReporte>().EjecutarAsync(id, datos, operador.Id, CancellationToken.None)));
        return id;
    }

    // Fija las marcas relativas al registro del caso.
    private Task FijarAsync(Guid id, TimeSpan? decision = null, TimeSpan? atencion = null) => fabrica.ConBaseDatosAsync(async db =>
    {
        var registrado = await db.Casos.Where(c => c.Id == id).Select(c => c.RegistradoEn).SingleAsync();
        if (decision is { } d)
            await db.DecisionesReporte.Where(x => x.CasoId == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.DecididaEn, registrado + d));
        if (atencion is { } a)
            await db.EventosCaso.Where(e => e.CasoId == id && e.Tipo == TipoEventoCaso.AtencionIniciada)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.OcurridoEn, registrado + a));
        return true;
    });

    private static string Celda(string html, Guid id, string atributo)
    {
        var fila = html.IndexOf($"data-caso=\"{id}\"", StringComparison.Ordinal);
        Assert.True(fila >= 0, "El caso no aparece en la búsqueda.");
        var inicio = html.IndexOf('>', html.IndexOf(atributo, fila, StringComparison.Ordinal)) + 1;
        return System.Net.WebUtility.HtmlDecode(html[inicio..html.IndexOf("</td>", inicio, StringComparison.Ordinal)]);
    }

    private static string Dato(string html, string atributo)
    {
        var inicio = html.IndexOf('>', html.IndexOf(atributo, StringComparison.Ordinal)) + 1;
        return System.Net.WebUtility.HtmlDecode(html[inicio..html.IndexOf("</dd>", inicio, StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task CA01_SosConRegistroEInicioDeAtencion_MuestraLaDiferenciaEnElDetalleYEnLaBusqueda()
    {
        var (id, sereno) = await SosAsync();
        await AtenderAsync(id, sereno);
        await FijarAsync(id, atencion: new TimeSpan(0, 3, 20));
        var operador = await OperadorAsync();

        Assert.Equal("3 min 20 s", Dato(await operador.GetStringAsync($"/panel/central/sos/{id}"), "data-tiempo-respuesta"));
        var busqueda = await operador.GetStringAsync("/panel/casos?Origen=Sos&Estado=EnAtencion");
        Assert.Equal("3 min 20 s", Celda(busqueda, id, "data-tiempo-atencion"));
        Assert.Equal(TiemposCaso.NoCorresponde, Celda(busqueda, id, "data-tiempo-decision"));
    }

    [Fact]
    public async Task CA02_ReporteAdmitidoYAtendido_MuestraPorSeparadoHastaLaDecisionYHastaLaAtencion()
    {
        var id = await ReporteDecididoAsync(TipoDecisionReporte.Admision);
        await AtenderAsync(id, await fabrica.CrearAsync(Roles.Sereno));
        await FijarAsync(id, decision: TimeSpan.FromMinutes(10), atencion: new TimeSpan(1, 2, 3));
        var operador = await OperadorAsync();

        var detalle = await operador.GetStringAsync($"/panel/reportes/{id}");
        Assert.Equal("10 min 00 s", Dato(detalle, "data-tiempo-decision"));
        Assert.Equal("1 h 02 min 03 s", Dato(detalle, "data-tiempo-atencion"));
        Assert.DoesNotContain("data-tiempo-respuesta", detalle);
        var busqueda = await operador.GetStringAsync("/panel/casos?Origen=Reporte&Estado=EnAtencion");
        Assert.Equal("10 min 00 s", Celda(busqueda, id, "data-tiempo-decision"));
        Assert.Equal("1 h 02 min 03 s", Celda(busqueda, id, "data-tiempo-atencion"));
    }

    [Fact]
    public async Task CA02_ReporteDerivado_MuestraLaDecisionYLaAtencionNoCorresponde()
    {
        var id = await ReporteDecididoAsync(TipoDecisionReporte.Derivacion);
        await FijarAsync(id, decision: TimeSpan.FromSeconds(42));

        var detalle = await (await OperadorAsync()).GetStringAsync($"/panel/reportes/{id}");

        Assert.Equal("42 s", Dato(detalle, "data-tiempo-decision"));
        Assert.Equal(TiemposCaso.NoCorresponde, Dato(detalle, "data-tiempo-atencion"));
    }

    [Fact]
    public async Task CA03_CasoSinMarcas_IndicaNoDisponible()
    {
        var (sos, _) = await SosAsync();
        var reporte = await ReporteDecididoAsync(TipoDecisionReporte.Admision); // admitido, todavía sin atención
        var operador = await OperadorAsync();

        Assert.Equal(TiemposCaso.NoDisponible, Dato(await operador.GetStringAsync($"/panel/central/sos/{sos}"), "data-tiempo-respuesta"));
        Assert.Equal(TiemposCaso.NoDisponible, Dato(await operador.GetStringAsync($"/panel/reportes/{reporte}"), "data-tiempo-atencion"));
        Assert.Equal(TiemposCaso.NoDisponible, Celda(await operador.GetStringAsync("/panel/casos?Origen=Sos&Estado=Pendiente"), sos, "data-tiempo-atencion"));
    }

    [Fact]
    public async Task CA04_ElTiempoDeRespuestaNoEsElDePresentacionEnLaCentralDeRnf02()
    {
        var (id, sereno) = await SosAsync();
        var operadorId = (await fabrica.CrearAsync(Roles.Operador)).Id;
        await fabrica.ConBaseDatosAsync(async db =>
        {
            var caso = await db.Casos.SingleAsync(c => c.Id == id);
            db.MedicionesPresentacion.Add(MedicionPresentacion.Registrar(id, operadorId, caso.RegistradoEn, caso.RegistradoEn.AddSeconds(2)));
            return await db.SaveChangesAsync();
        });
        var operador = await OperadorAsync();

        // Presentado en la central a los 2 s, pero sin inicio de atención: no hay tiempo de respuesta.
        Assert.Equal(TiemposCaso.NoDisponible, Dato(await operador.GetStringAsync($"/panel/central/sos/{id}"), "data-tiempo-respuesta"));

        await AtenderAsync(id, sereno);
        await FijarAsync(id, atencion: TimeSpan.FromMinutes(9));
        var html = await operador.GetStringAsync($"/panel/central/sos/{id}");
        Assert.Equal("9 min 00 s", Dato(html, "data-tiempo-respuesta"));
        Assert.Contains("No es el tiempo de presentación en la central.", System.Net.WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task ConReasignacion_CuentaElPrimerInicioDeAtencion()
    {
        var (id, sereno) = await SosAsync();
        await AtenderAsync(id, sereno);
        await FijarAsync(id, atencion: TimeSpan.FromMinutes(5));
        // Un segundo inicio de atención (p. ej., tras cancelar la asignación, DP03) no cambia el tiempo de respuesta.
        await fabrica.ConBaseDatosAsync(async db =>
        {
            var caso = await db.Casos.SingleAsync(c => c.Id == id);
            db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.AtencionIniciada, sereno.Id, VisibilidadEvento.Interna, caso.RegistradoEn.AddMinutes(30)));
            return await db.SaveChangesAsync();
        });

        Assert.Equal("5 min 00 s", (await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarTiemposCaso>().EjecutarAsync(id, CancellationToken.None)))!.Respuesta);
        Assert.Equal("5 min 00 s", Celda(await (await OperadorAsync()).GetStringAsync("/panel/casos?Origen=Sos&Estado=EnAtencion"), id, "data-tiempo-atencion"));
    }
}
