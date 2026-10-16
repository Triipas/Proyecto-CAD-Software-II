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
        new("GET", "/salud/lista", "TEC01", Acceso.Publico),
        new("POST", "/api/cuentas/registro", "HU01", Acceso.Publico),
        // Con cuerpo vacío el propio caso de uso responde 401 por credenciales (no es un rechazo de autorización).
        new("POST", "/api/auth/token", "HU02", Acceso.Publico, Contenido: "Correo o contraseña incorrectos.", EstadoPropio: 401),
        new("GET", "/api/cuentas/yo", "HU02", Acceso.CualquierSesion, Contenido: "\"rol\""),
        // Con cuerpo vacío el ciudadano llega al caso de uso (400 por falta de IdSolicitud); el resto es rechazado antes.
        new("POST", "/api/sos", "HU07", Acceso.SoloRol, Roles.Ciudadano),
        new("POST", "/api/reportes", "HU10", Acceso.SoloRol, Roles.Ciudadano),
        new("GET", "/api/reportes/categorias", "HU10", Acceso.SoloRol, Roles.Ciudadano, Contenido: "Violencia doméstica"),
        // HU30 — mis casos. Con un caso inexistente (o ajeno) el ciudadano recibe 404; el resto es rechazado antes.
        new("GET", "/api/casos/mios", "HU30", Acceso.SoloRol, Roles.Ciudadano),
        new("GET", "/api/casos/mios/{id:guid}", "HU30", Acceso.SoloRol, Roles.Ciudadano,
            Ejemplo: "/api/casos/mios/00000000-0000-0000-0000-000000000000"),
        // HU31 — bandeja de notificaciones. Con una notificación inexistente (o ajena) el ciudadano recibe 404.
        new("GET", "/api/notificaciones", "HU31", Acceso.SoloRol, Roles.Ciudadano),
        new("POST", "/api/notificaciones/{id:guid}/leida", "HU31", Acceso.SoloRol, Roles.Ciudadano,
            Ejemplo: "/api/notificaciones/00000000-0000-0000-0000-000000000000/leida"),
        // RNF02: resultados de la medición. Sin el parámetro «desde», el administrador recibe 400 (llega al endpoint).
        new("GET", "/api/mediciones/presentacion", "RNF02", Acceso.SoloRol, Roles.Administrador),
        new("GET", "/panel/ingresar", "HU02", Acceso.Publico),
        new("POST", "/panel/salir", "HU02", Acceso.Publico),
        new("GET", "/panel/acceso-denegado", "HU03", Acceso.Publico, Contenido: "Tu rol no tiene permiso", EstadoPropio: 403),
        new("GET", "/panel/central", "HU11", Acceso.SoloRol, Roles.Operador, "Atención urgente · SOS"),
        // Canal en tiempo real de la central: solo operadores. El operador negocia (200); el resto, 401/403.
        new("POST", "/hubs/central/negotiate", "HU11", Acceso.SoloRol, Roles.Operador, Ejemplo: "/hubs/central/negotiate?negotiateVersion=1"),
        // Sin identificador de conexión el operador recibe 400 (llega al hub); el resto es rechazado antes.
        new("GET", "/hubs/central", "HU11", Acceso.SoloRol, Roles.Operador),
        // HU12 — verificación del SOS. Con un SOS inexistente el operador llega a la página (404); al enviar
        // sin token antifalsificación recibe 400. El resto es rechazado antes, sin ver ni registrar nada.
        new("GET", "/panel/central/sos/{id:guid}", "HU12", Acceso.SoloRol, Roles.Operador,
            Ejemplo: "/panel/central/sos/00000000-0000-0000-0000-000000000000"),
        new("POST", "/panel/central/sos/{id:guid}", "HU12", Acceso.SoloRol, Roles.Operador,
            Ejemplo: "/panel/central/sos/00000000-0000-0000-0000-000000000000"),
        // HU14 — página «Reportes» (E5) y detalle de un reporte. Con un reporte inexistente el operador llega a la página
        // (404); al enviar sin token antifalsificación recibe 400. El resto es rechazado antes, sin ver ni registrar nada.
        new("GET", "/panel/reportes", "HU14", Acceso.SoloRol, Roles.Operador),
        new("GET", "/panel/reportes/{id:guid}", "HU14", Acceso.SoloRol, Roles.Operador,
            Ejemplo: "/panel/reportes/00000000-0000-0000-0000-000000000000"),
        new("POST", "/panel/reportes/{id:guid}", "HU14", Acceso.SoloRol, Roles.Operador,
            Ejemplo: "/panel/reportes/00000000-0000-0000-0000-000000000000"),
        // HU26 — búsqueda de casos con filtros (solo operador, CA06).
        new("GET", "/panel/casos", "HU26", Acceso.SoloRol, Roles.Operador),
        // HU27 — historial de un caso. Con un caso inexistente el operador llega a la página (404); el resto, rechazado antes.
        new("GET", "/panel/casos/{id:guid}/historial", "HU27", Acceso.SoloRol, Roles.Operador,
            Ejemplo: "/panel/casos/00000000-0000-0000-0000-000000000000/historial"),
        // HU18 — estado operativo y posiciones del sereno (canal de la app). Sin cuerpo válido el sereno
        // llega al endpoint (400); el resto es rechazado antes, sin crear estado ni posiciones.
        new("GET", "/api/sereno/estado", "HU18", Acceso.SoloRol, Roles.Sereno, Contenido: "intervaloPosicionesSegundos"),
        new("PUT", "/api/sereno/estado", "HU18", Acceso.SoloRol, Roles.Sereno),
        new("POST", "/api/sereno/posiciones", "HU18", Acceso.SoloRol, Roles.Sereno),
        // HU21 — despacho del sereno. Sin despacho el sereno recibe 204; con un caso que no es suyo, 404 (CA04).
        // El resto es rechazado antes. El dispositivo (ADR-07) solo lo registra o quita el propio sereno.
        new("GET", "/api/sereno/despacho", "HU21", Acceso.SoloRol, Roles.Sereno),
        new("GET", "/api/sereno/casos/{id:guid}", "HU21", Acceso.SoloRol, Roles.Sereno,
            Ejemplo: "/api/sereno/casos/00000000-0000-0000-0000-000000000000"),
        // HU22 y HU23 — avances del sereno. Con un caso que no es suyo el sereno recibe 404; el resto, rechazado antes.
        new("POST", "/api/sereno/casos/{id:guid}/desplazamiento", "HU22", Acceso.SoloRol, Roles.Sereno,
            Ejemplo: "/api/sereno/casos/00000000-0000-0000-0000-000000000000/desplazamiento"),
        new("POST", "/api/sereno/casos/{id:guid}/atencion", "HU23", Acceso.SoloRol, Roles.Sereno,
            Ejemplo: "/api/sereno/casos/00000000-0000-0000-0000-000000000000/atencion"),
        // HU24 — cierre por el sereno (404 con un caso que no es suyo) y catálogo para la tipificación final.
        new("POST", "/api/sereno/casos/{id:guid}/cierre", "HU24", Acceso.SoloRol, Roles.Sereno,
            Ejemplo: "/api/sereno/casos/00000000-0000-0000-0000-000000000000/cierre"),
        new("GET", "/api/sereno/categorias", "HU24", Acceso.SoloRol, Roles.Sereno, Contenido: "Violencia doméstica"),
        new("PUT", "/api/sereno/dispositivo", "HU21", Acceso.SoloRol, Roles.Sereno),
        new("DELETE", "/api/sereno/dispositivo", "HU21", Acceso.SoloRol, Roles.Sereno),
        // Canal en tiempo real del sereno (HU21-CA01): solo serenos, con su JWT.
        new("POST", "/hubs/sereno/negotiate", "HU21", Acceso.SoloRol, Roles.Sereno, Ejemplo: "/hubs/sereno/negotiate?negotiateVersion=1"),
        new("GET", "/hubs/sereno", "HU21", Acceso.SoloRol, Roles.Sereno),
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
