using System.Text.RegularExpressions;
using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Infraestructura;

public sealed record CuentaDePrueba(Guid Id, string Correo, string Clave, string Rol);

public static partial class CuentasDePrueba
{
    // Crea directamente una cuenta del rol indicado (HU04 aún no existe para operadores y serenos).
    public static async Task<CuentaDePrueba> CrearAsync(this FabricaCad fabrica, string rol, bool activa = true)
    {
        var correo = $"{rol.ToLowerInvariant()}.{Guid.NewGuid():N}@correo.test";
        var clave = $"Clave-{Guid.NewGuid():N}";

        return await fabrica.ConServiciosAsync(async servicios =>
        {
            var usuarios = servicios.GetRequiredService<UserManager<Usuario>>();
            var usuario = new Usuario
            {
                Id = Guid.CreateVersion7(),
                UserName = correo,
                Email = correo,
                Nombres = "Prueba",
                Apellidos = rol,
                Activo = activa,
                CreadoEn = DateTimeOffset.UtcNow,
            };
            Comprobar(await usuarios.CreateAsync(usuario, clave));
            Comprobar(await usuarios.AddToRoleAsync(usuario, rol));
            return new CuentaDePrueba(usuario.Id, correo, clave, rol);
        });
    }

    // Cliente de navegador: HTTPS (la cookie del panel es Secure fuera de desarrollo) y sin seguir redirecciones.
    public static HttpClient CrearNavegador(this FabricaCad fabrica) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    public static async Task<HttpResponseMessage> IngresarAlPanelAsync(this HttpClient navegador, string correo, string clave)
    {
        var token = await ObtenerTokenAntifalsificacionAsync(navegador, "/panel/ingresar");
        return await navegador.PostAsync("/panel/ingresar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Formulario.Correo"] = correo,
            ["Formulario.Contrasena"] = clave,
            ["__RequestVerificationToken"] = token,
        }));
    }

    public static async Task<string> ObtenerTokenAntifalsificacionAsync(HttpClient navegador, string pagina)
    {
        var html = await navegador.GetStringAsync(pagina);
        var coincidencia = TokenAntifalsificacion().Match(html);
        Assert.True(coincidencia.Success, $"La página {pagina} no contiene un token antifalsificación.");
        return coincidencia.Groups[1].Value;
    }

    public static int Contar(string texto, string fragmento) =>
        (texto.Length - texto.Replace(fragmento, "").Length) / fragmento.Length;

    private static void Comprobar(IdentityResult resultado) =>
        Assert.True(resultado.Succeeded, string.Join(" ", resultado.Errors.Select(e => e.Code)));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenAntifalsificacion();
}
