using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Autorizacion;

// HU03 — Acceder solo a las funciones y datos de mi rol (S1-T09, S1-T19).
// Matriz ejecutable: cada operación existente se prueba sin sesión y con los cuatro roles.
// CA04 es acumulativo: cada HU que añada una ruta debe declararla aquí, o falla
// TodasLasRutasDelServidor_EstanDeclaradasEnLaMatriz. Ver docs/matriz-permisos.md.
[Collection(ColeccionServidor.Nombre)]
public class MatrizAutorizacionTests(FabricaCad fabrica)
{
    public enum Acceso { Publico, CualquierSesion, SoloRol }

    // Contenido = texto que solo debe ver quien tiene permiso (CA03: sin información restringida).
    // EstadoPropio: código con el que la operación responde cuando se ejecuta (la página de acceso denegado responde 403).
    public sealed record Operacion(string Metodo, string Ruta, string Hu, Acceso Acceso, string? Rol = null, string? Contenido = null, int? EstadoPropio = null, string? Ejemplo = null);

    public static readonly Operacion[] Matriz =
    [
        new("GET", "/salud", "TEC01", Acceso.Publico),
        new("POST", "/api/cuentas/registro", "HU01", Acceso.Publico),
        // Con cuerpo vacío el propio caso de uso responde 401 por credenciales (no es un rechazo de autorización).
        new("POST", "/api/auth/token", "HU02", Acceso.Publico, Contenido: "Correo o contraseña incorrectos.", EstadoPropio: 401),
        new("GET", "/api/cuentas/yo", "HU02", Acceso.CualquierSesion, Contenido: "\"rol\""),
        new("GET", "/panel/ingresar", "HU02", Acceso.Publico),
        new("POST", "/panel/salir", "HU02", Acceso.Publico),
        new("GET", "/panel/acceso-denegado", "HU03", Acceso.Publico, Contenido: "Tu rol no tiene permiso", EstadoPropio: 403),
        new("GET", "/panel/central", "HU11 (base)", Acceso.SoloRol, Roles.Operador, "Central de monitoreo"),
        new("GET", "/panel/admin", "HU04 (base)", Acceso.SoloRol, Roles.Administrador, "Interfaz del administrador"),
        new("GET", "/panel/admin/cuentas", "HU04", Acceso.SoloRol, Roles.Administrador, "Cuentas institucionales"),
        new("GET", "/panel/admin/cuentas/nueva", "HU04", Acceso.SoloRol, Roles.Administrador, "Nueva cuenta institucional"),
        // Un identificador inexistente basta: el administrador llega a la página (404) y el resto es rechazado antes.
        new("GET", "/panel/admin/cuentas/{id:guid}/editar", "HU04", Acceso.SoloRol, Roles.Administrador,
            Ejemplo: "/panel/admin/cuentas/00000000-0000-0000-0000-000000000000/editar"),
    ];

    private static readonly string?[] Perfiles = [null, Roles.Ciudadano, Roles.Operador, Roles.Sereno, Roles.Administrador];

    public static TheoryData<string, string> Casos()
    {
        var datos = new TheoryData<string, string>();
        foreach (var operacion in Matriz)
            foreach (var perfil in Perfiles)
                datos.Add($"{operacion.Metodo} {operacion.Ruta}", perfil ?? "Anónimo");
        return datos;
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task CA01_CA02_CA03_CadaPerfilSoloAccedeASusOperaciones(string clave, string perfil)
    {
        var operacion = Matriz.Single(o => $"{o.Metodo} {o.Ruta}" == clave);
        var rol = perfil == "Anónimo" ? null : perfil;
        var debePermitir = operacion.Acceso switch
        {
            Acceso.Publico => true,
            Acceso.CualquierSesion => rol is not null,
            _ => rol == operacion.Rol,
        };

        var cliente = await ClienteDeAsync(rol);
        var respuesta = await cliente.SendAsync(new HttpRequestMessage(new HttpMethod(operacion.Metodo), operacion.Ejemplo ?? operacion.Ruta)
        {
            Content = operacion.Metodo == "POST" ? JsonContent.Create(new { }) : null,
        });
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        var destino = respuesta.Headers.Location?.ToString() ?? "";
        var rechazada = (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                && (int)respuesta.StatusCode != operacion.EstadoPropio)
            || (respuesta.StatusCode == HttpStatusCode.Redirect
                && (destino.Contains("/panel/ingresar") || destino.Contains("/panel/acceso-denegado")));

        if (debePermitir)
        {
            Assert.False(rechazada, $"{clave} debería estar disponible para {perfil}, pero respondió {(int)respuesta.StatusCode} {destino}");
            if (operacion.Contenido is not null)
                Assert.Contains(operacion.Contenido, cuerpo); // CA01: la operación se ejecuta
        }
        else
        {
            Assert.True(rechazada, $"{clave} debería rechazarse para {perfil}, pero respondió {(int)respuesta.StatusCode} {destino}");
            if (operacion.Contenido is not null)
                Assert.DoesNotContain(operacion.Contenido, cuerpo); // CA03: sin información restringida
        }
    }

    [Fact]
    public void CA04_TodasLasRutasDelServidor_EstanDeclaradasEnLaMatriz()
    {
        var rutasDelServidor = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .ToHashSet();
        var rutasDeclaradas = Matriz.Select(o => o.Ruta).ToHashSet();

        var sinDeclarar = rutasDelServidor.Except(rutasDeclaradas).Order().ToList();
        var inexistentes = rutasDeclaradas.Except(rutasDelServidor).Order().ToList();

        Assert.True(sinDeclarar.Count == 0, "Rutas sin permisos declarados en la matriz: " + string.Join(", ", sinDeclarar));
        Assert.True(inexistentes.Count == 0, "La matriz declara rutas que ya no existen: " + string.Join(", ", inexistentes));
    }

    [Fact]
    public async Task DenegacionPorDefecto_RutaApiNoDeclaradaSinSesion_Responde401()
    {
        // Con la política por defecto, una ruta /api desconocida exige sesión (401), igual que una existente:
        // no se revela qué rutas existen.
        var respuesta = await fabrica.CrearNavegador().GetAsync("/api/ruta-no-declarada");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    private async Task<HttpClient> ClienteDeAsync(string? rol)
    {
        var navegador = fabrica.CrearNavegador();
        if (rol is null)
            return navegador;

        var cuenta = await fabrica.CrearAsync(rol);
        if (rol is Roles.Ciudadano or Roles.Sereno)
        {
            // Canal de la app: JWT (HU02).
            var token = await (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token",
                new SolicitudInicioSesion(cuenta.Correo, cuenta.Clave))).Content.ReadFromJsonAsync<RespuestaToken>();
            navegador.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        }
        else
        {
            // Canal del panel: cookie (HU02).
            Assert.Equal(HttpStatusCode.Redirect, (await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave)).StatusCode);
        }
        return navegador;
    }
}
