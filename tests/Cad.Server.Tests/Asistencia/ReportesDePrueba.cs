using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cad.Server.Tests.Asistencia;

// Reportes de ciudadanos registrados por la API, como en la app, contra el servidor que diga cada prueba. Datos ficticios.
public static class ReportesDePrueba
{
    // Un punto al azar en Arequipa, para que los reportes de otras pruebas no caigan cerca (candidatos a duplicado).
    public static (double Lat, double Lon) PuntoAlAzar() => (-16.45 + Random.Shared.NextDouble() * 0.1, -71.60 + Random.Shared.NextDouble() * 0.1);

    public static async Task<HttpClient> CiudadanoAsync(FabricaCad fabrica, WebApplicationFactory<Program> servidor)
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var token = await (await servidor.CreateClient().PostAsJsonAsync("/api/auth/token",
            new Cad.Contratos.Cuentas.SolicitudInicioSesion(cuenta.Correo, cuenta.Clave))).Content.ReadFromJsonAsync<Cad.Contratos.Cuentas.RespuestaToken>();
        var app = servidor.CreateClient();
        app.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Token);
        return app;
    }

    public static async Task<Guid> RegistrarAsync(HttpClient app, string descripcion, Guid categoria, bool conPunto = true)
    {
        var (lat, lon) = PuntoAlAzar();
        var ubicacion = conPunto
            ? new UbicacionHecho(FuentesUbicacionHecho.Mapa, lat, lon, null, "Frente al parque")
            : new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, "Frente al parque del barrio");
        var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(), categoria, descripcion,
            DateTimeOffset.UtcNow.AddMinutes(-10), ubicacion));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }
}
