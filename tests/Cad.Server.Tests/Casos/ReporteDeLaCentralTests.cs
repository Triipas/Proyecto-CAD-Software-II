using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Casos;

// Reporte registrado por la central (23/10/2026): sin ciudadano, con quién avisó si lo dio, y ya admitido para asignarlo.
// Datos ficticios (puntos en Arequipa).
[Collection(ColeccionServidor.Nombre)]
public class ReporteDeLaCentralTests(FabricaCad fabrica)
{
    private const string Ruta = "/panel/reportes/nuevo";
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private const string Descripcion = "Llamaron para avisar que forzaban la puerta de una tienda.";

    private async Task<HttpClient> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task<HttpResponseMessage> RegistrarAsync(HttpClient operador, Guid? idSolicitud = null, string? descripcion = Descripcion,
        string? latitud = "-16.398800", string? longitud = "-71.536900", string? referencia = null, string? contacto = "Vecina ficticia",
        string? telefono = "987 654 321", string? ocurrido = null, string categoria = "")
    {
        var campos = new Dictionary<string, string>
        {
            ["Reporte.IdSolicitud"] = (idSolicitud ?? Guid.NewGuid()).ToString(),
            ["Reporte.CategoriaId"] = categoria == "" ? Robo.ToString() : categoria,
            ["Reporte.Descripcion"] = descripcion ?? "",
            ["Reporte.OcurridoEn"] = ocurrido ?? DateTime.UtcNow.AddHours(-6).AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm"),
            ["Reporte.Latitud"] = latitud ?? "",
            ["Reporte.Longitud"] = longitud ?? "",
            ["Reporte.Referencia"] = referencia ?? "",
            ["Reporte.ContactoNombre"] = contacto ?? "",
            ["Reporte.ContactoTelefono"] = telefono ?? "",
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, Ruta),
        };
        return await operador.PostAsync(Ruta, new FormUrlEncodedContent(campos));
    }

    private static Guid IdDe(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return Guid.Parse(respuesta.Headers.Location!.ToString().Split('/').Last());
    }

    [Fact]
    public async Task ConPuntoYContacto_QuedaAdmitidoSinCiudadano_YAparecePorAsignarEnLaCentral()
    {
        var operador = await PanelAsync();

        var id = IdDe(await RegistrarAsync(operador));

        var caso = await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.Equal(OrigenCaso.Reporte, caso.Origen);
        Assert.Equal(EstadoCaso.Pendiente, caso.Estado);
        Assert.Null(caso.CiudadanoId);
        var detalle = await fabrica.ConBaseDatosAsync(db => db.DetallesReporte.AsNoTracking().SingleAsync(d => d.CasoId == id));
        Assert.Equal(("Mapa", -16.3988, -71.5369), (detalle.FuenteUbicacion, detalle.LatitudHecho!.Value, detalle.LongitudHecho!.Value));
        Assert.Equal(("Vecina ficticia", "987 654 321"), (detalle.ContactoNombre, detalle.ContactoTelefono));
        Assert.NotNull(detalle.RegistradoPorOperadorId);
        var decision = await fabrica.ConBaseDatosAsync(db => db.DecisionesReporte.AsNoTracking().SingleAsync(d => d.CasoId == id));
        Assert.Equal(TipoDecisionReporte.Admision, decision.Tipo);
        Assert.Equal(RegistrarReporte.MotivoAdmisionCentral, decision.Motivo);

        // En «Reportes admitidos» de la central, en el detalle (con quién avisó), en «Casos» y en el historial.
        Assert.Contains(id.ToString(), await operador.GetStringAsync("/panel/central?handler=Reportes"));
        var pagina = await operador.GetStringAsync($"/panel/reportes/{id}");
        Assert.Contains("Por la central", pagina);
        Assert.Contains("Vecina ficticia · 987 654 321", pagina);
        Assert.Contains(id.ToString(), await operador.GetStringAsync("/panel/casos"));
        Assert.Contains("Reporte registrado por la central", await operador.GetStringAsync($"/panel/casos/{id}/historial"));
    }

    [Fact]
    public async Task SoloConReferencia_SinContacto_SeRegistraIgual()
    {
        var operador = await PanelAsync();

        var id = IdDe(await RegistrarAsync(operador, latitud: null, longitud: null, referencia: "Frente a la plaza, junto al quiosco", contacto: null, telefono: null));

        var detalle = await fabrica.ConBaseDatosAsync(db => db.DetallesReporte.AsNoTracking().SingleAsync(d => d.CasoId == id));
        Assert.Equal(("Texto", (double?)null, "Frente a la plaza, junto al quiosco"), (detalle.FuenteUbicacion, detalle.LatitudHecho, detalle.ReferenciaHecho));
        Assert.Null(detalle.ContactoNombre);
        Assert.Contains("Registrado por la central", await operador.GetStringAsync("/panel/central?handler=Reportes"));
    }

    [Fact]
    public async Task SinDatosObligatorios_NoSeRegistra_YSeExplicaCadaCampo()
    {
        var operador = await PanelAsync();
        var antes = await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync());

        var respuesta = await RegistrarAsync(operador, descripcion: "  ", latitud: null, longitud: null, referencia: null, telefono: "abc", categoria: Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("Elige un tipo de incidencia del catálogo.", html);
        Assert.Contains("Describe lo ocurrido.", html);
        Assert.Contains("Escribe una referencia del lugar del hecho.", html);
        Assert.Contains("Escribe el teléfono solo con números", html);
        Assert.Equal(antes, await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync()));
    }

    [Fact]
    public async Task UnaFechaFutura_SeRechaza()
    {
        var operador = await PanelAsync();

        var respuesta = await RegistrarAsync(operador, ocurrido: DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm"));

        Assert.Contains("no pueden ser futuras", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ElMismoEnvioDosVeces_CreaUnSoloReporte()
    {
        var operador = await PanelAsync();
        var idSolicitud = Guid.NewGuid();

        var primero = IdDe(await RegistrarAsync(operador, idSolicitud));
        var segundo = IdDe(await RegistrarAsync(operador, idSolicitud));

        Assert.Equal(primero, segundo);
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync(c => c.IdSolicitud == idSolicitud)));
    }

    [Fact]
    public async Task SeAsignaYSeCierra_ComoCualquierReporte_SinAvisarANingunCiudadano()
    {
        var operador = await PanelAsync();
        var id = IdDe(await RegistrarAsync(operador));
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var sereno = await fabrica.CrearClienteAppAsync(cuenta);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);

        var ruta = $"/panel/reportes/{id}";
        Assert.Equal(HttpStatusCode.Redirect, (await operador.PostAsync($"{ruta}?handler=Asignar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Asignacion.SerenoId"] = cuenta.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(),
            ["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(operador, ruta),
        }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.GetAsync("/api/sereno/despacho")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsync($"/api/sereno/casos/{id}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsJsonAsync($"/api/sereno/casos/{id}/atencion", await fabrica.EnElLugarAsync(id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sereno.PostAsJsonAsync($"/api/sereno/casos/{id}/cierre",
            new SolicitudCierreCaso(Robo, "Se verificó el lugar; la tienda estaba cerrada y sin daños.", null))).StatusCode);

        Assert.Equal(EstadoCaso.Cerrada, (await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().SingleAsync(c => c.Id == id))).Estado);
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.NotificacionesCiudadano.AnyAsync(n => n.CasoId == id)));
    }

    [Fact]
    public async Task OtroPerfilDelPanel_NoRegistraReportes()
    {
        var administrador = await PanelAsync(Roles.Administrador);

        var respuesta = await administrador.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/panel/acceso-denegado", respuesta.Headers.Location?.ToString());
    }

    [Fact]
    public async Task UnSosSinCiudadano_LoRechazaLaBaseDeDatos()
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));
        var sos = await (await ciudadano.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>();

        var error = await Record.ExceptionAsync(() => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Casos\" SET \"CiudadanoId\" = NULL WHERE \"Id\" = {sos!.Id}")));

        Assert.NotNull(error);
    }
}
