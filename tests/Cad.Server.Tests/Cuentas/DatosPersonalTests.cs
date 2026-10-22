using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Cuentas;

// Datos del personal (PO, 22/10/2026): DNI obligatorio y único, código de personal generado por el sistema, teléfono
// obligatorio para el sereno y cambio obligatorio de la contraseña que fijó el administrador. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public partial class DatosPersonalTests(FabricaCad fabrica)
{
    private const string RutaListado = "/panel/admin/cuentas";
    private const string RutaNueva = "/panel/admin/cuentas/nueva";
    private const string RutaCambio = "/panel/cambiar-contrasena";
    private static string RutaEditar(Guid id) => $"/panel/admin/cuentas/{id}/editar";
    private static string CorreoUnico() => $"personal.{Guid.NewGuid():N}@correo.test";
    private static string DniUnico() => Random.Shared.Next(10_000_000, 100_000_000).ToString(CultureInfo.InvariantCulture);

    private async Task<HttpClient> AdministradorAsync()
    {
        var navegador = fabrica.CrearNavegador();
        Assert.Equal(HttpStatusCode.Redirect, (await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave)).StatusCode);
        return navegador;
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient navegador, string pagina, string ruta, Dictionary<string, string> campos)
    {
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, pagina);
        return await navegador.PostAsync(ruta, new FormUrlEncodedContent(campos));
    }

    private static Dictionary<string, string> Alta(string correo, string rol, string dni, string telefono = "912345678") => new()
    {
        ["Formulario.Nombres"] = "Rosa",
        ["Formulario.Apellidos"] = "Quispe Huamán",
        ["Formulario.Correo"] = correo,
        ["Formulario.Dni"] = dni,
        ["Formulario.Telefono"] = telefono,
        ["Formulario.Rol"] = rol,
        ["Formulario.Contrasena"] = "ClaveInicial2026",
    };

    private Task<Usuario> CuentaAsync(string correo) => fabrica.ConBaseDatosAsync(db =>
        db.Users.AsNoTracking().SingleAsync(u => u.NormalizedEmail == correo.ToUpperInvariant()));

    private async Task<Usuario> CrearPorPanelAsync(HttpClient admin, string rol, string? dni = null)
    {
        var correo = CorreoUnico();
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(admin, RutaNueva, RutaNueva, Alta(correo, rol, dni ?? DniUnico()))).StatusCode);
        return await CuentaAsync(correo);
    }

    // ----- DNI, código y teléfono -----

    [Fact]
    public async Task ElCodigo_LoGeneraElSistema_CorrelativoPorRol()
    {
        var admin = await AdministradorAsync();

        var primero = await CrearPorPanelAsync(admin, Roles.Sereno);
        var segundo = await CrearPorPanelAsync(admin, Roles.Sereno);
        var operador = await CrearPorPanelAsync(admin, Roles.Operador);

        int Numero(string codigo, string prefijo) => int.Parse(codigo[prefijo.Length..], CultureInfo.InvariantCulture);
        Assert.StartsWith("S-", primero.CodigoPersonal);
        Assert.Equal(Numero(primero.CodigoPersonal!, "S-") + 1, Numero(segundo.CodigoPersonal!, "S-"));
        Assert.StartsWith("OP-", operador.CodigoPersonal);
        // El listado confirma el código asignado (mensaje de la última alta).
        Assert.Contains($"con el código {operador.CodigoPersonal}", await admin.GetStringAsync(RutaListado));
    }

    [Fact]
    public async Task ElCodigoNoSeEscribe_UnValorEnviadoSeIgnora()
    {
        var admin = await AdministradorAsync();
        var correo = CorreoUnico();
        var datos = Alta(correo, Roles.Operador, DniUnico());
        datos["Formulario.CodigoPersonal"] = "OP-999999";

        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(admin, RutaNueva, RutaNueva, datos)).StatusCode);
        Assert.NotEqual("OP-999999", (await CuentaAsync(correo)).CodigoPersonal);
    }

    [Fact]
    public async Task UnDniRepetido_SeRechazaAlCrearYAlModificar()
    {
        var admin = await AdministradorAsync();
        var dni = DniUnico();
        var existente = await CrearPorPanelAsync(admin, Roles.Operador, dni);
        var total = await fabrica.ConBaseDatosAsync(db => db.Users.CountAsync());

        var alta = await EnviarAsync(admin, RutaNueva, RutaNueva, Alta(CorreoUnico(), Roles.Sereno, dni));
        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        Assert.Contains("Ya existe una cuenta con ese DNI.", await alta.Content.ReadAsStringAsync());
        Assert.Equal(total, await fabrica.ConBaseDatosAsync(db => db.Users.CountAsync()));

        var otra = await CrearPorPanelAsync(admin, Roles.Operador);
        var cambio = await EnviarAsync(admin, RutaEditar(otra.Id), RutaEditar(otra.Id), new()
        {
            ["Formulario.Nombres"] = otra.Nombres, ["Formulario.Apellidos"] = otra.Apellidos, ["Formulario.Correo"] = otra.Email!,
            ["Formulario.Dni"] = dni, ["Formulario.Telefono"] = "",
        });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        Assert.Contains("Ya existe una cuenta con ese DNI.", await cambio.Content.ReadAsStringAsync());
        Assert.NotEqual(dni, (await CuentaAsync(otra.Email!)).Dni);
        Assert.Equal(dni, (await CuentaAsync(existente.Email!)).Dni);
    }

    [Fact]
    public async Task ElTelefono_EsObligatorioParaElSereno_YOpcionalParaElOperador()
    {
        var admin = await AdministradorAsync();

        var sereno = await EnviarAsync(admin, RutaNueva, RutaNueva, Alta(CorreoUnico(), Roles.Sereno, DniUnico(), telefono: ""));
        Assert.Equal(HttpStatusCode.OK, sereno.StatusCode);
        Assert.Contains("El teléfono móvil es obligatorio.", await sereno.Content.ReadAsStringAsync());

        var operador = await EnviarAsync(admin, RutaNueva, RutaNueva, Alta(CorreoUnico(), Roles.Operador, DniUnico(), telefono: ""));
        Assert.Equal(HttpStatusCode.Redirect, operador.StatusCode);
    }

    [Fact]
    public async Task ElListado_MuestraCodigoYDniEnmascarado_YMarcaLasCuentasIncompletas()
    {
        var admin = await AdministradorAsync();
        var dni = DniUnico();
        var completa = await CrearPorPanelAsync(admin, Roles.Sereno, dni);
        var anterior = await fabrica.CrearAsync(Roles.Operador); // cuenta sin DNI ni código, como las anteriores al cambio

        var html = await admin.GetStringAsync(RutaListado);

        var fila = FilaDe(html, completa.Id);
        Assert.Contains($"<td data-codigo>{completa.CodigoPersonal}</td>", fila);
        Assert.Contains($"****{dni[^4..]}", fila);
        Assert.DoesNotContain(dni, fila); // el DNI completo solo en «Modificar»
        Assert.DoesNotContain("data-datos-incompletos", fila);
        Assert.Contains("data-datos-incompletos", FilaDe(html, anterior.Id));
        Assert.Contains("Faltan datos obligatorios", await admin.GetStringAsync(RutaEditar(anterior.Id)));
    }

    [Fact]
    public async Task LaBusqueda_EncuentraPorDniCompletoYPorCodigo_YFiltraPorRol()
    {
        var admin = await AdministradorAsync();
        var dni = DniUnico();
        var sereno = await CrearPorPanelAsync(admin, Roles.Sereno, dni);
        var operador = await CrearPorPanelAsync(admin, Roles.Operador);

        var porDni = await admin.GetStringAsync($"{RutaListado}?Buscar={dni}");
        Assert.Contains($"data-cuenta=\"{sereno.Id}\"", porDni);
        Assert.DoesNotContain($"data-cuenta=\"{operador.Id}\"", porDni);

        var porCodigo = await admin.GetStringAsync($"{RutaListado}?Buscar={operador.CodigoPersonal}");
        Assert.Contains($"data-cuenta=\"{operador.Id}\"", porCodigo);

        var serenos = await admin.GetStringAsync($"{RutaListado}?Rol=Sereno");
        Assert.Contains($"data-cuenta=\"{sereno.Id}\"", serenos);
        Assert.DoesNotContain($"data-cuenta=\"{operador.Id}\"", serenos);

        Assert.Contains("data-sin-resultados", await admin.GetStringAsync($"{RutaListado}?Buscar=nadie-se-llama-asi"));
    }

    // ----- Cambio obligatorio de la contraseña -----

    [Fact]
    public async Task OperadorConContrasenaProvisional_SoloPuedeCambiarla_YLuegoOperaConUnaSesionNueva()
    {
        var admin = await AdministradorAsync();
        var operador = await CrearPorPanelAsync(admin, Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        Assert.Equal(RutaCambio, (await navegador.IngresarAlPanelAsync(operador.Email!, "ClaveInicial2026")).Headers.Location?.OriginalString);

        // Cualquier página de operador lo devuelve al cambio.
        Assert.Equal(RutaCambio, (await navegador.GetAsync("/panel/central")).Headers.Location?.OriginalString);
        Assert.Equal(RutaCambio, (await navegador.GetAsync("/panel/casos")).Headers.Location?.OriginalString);

        async Task<HttpResponseMessage> Cambiar(string actual, string nueva, string confirmacion) =>
            await EnviarAsync(navegador, RutaCambio, RutaCambio, new()
            {
                ["Formulario.Actual"] = actual, ["Formulario.Nueva"] = nueva, ["Formulario.Confirmacion"] = confirmacion,
            });

        Assert.Contains("La contraseña actual no es correcta.", await (await Cambiar("Otra-clave-1", "NuevaClave2026", "NuevaClave2026")).Content.ReadAsStringAsync());
        Assert.Contains("debe ser distinta de la actual", await (await Cambiar("ClaveInicial2026", "ClaveInicial2026", "ClaveInicial2026")).Content.ReadAsStringAsync());
        Assert.Contains("Las contraseñas no coinciden.", await (await Cambiar("ClaveInicial2026", "NuevaClave2026", "NuevaClave2027")).Content.ReadAsStringAsync());
        Assert.True((await CuentaAsync(operador.Email!)).DebeCambiarContrasena);

        var correcto = await Cambiar("ClaveInicial2026", "NuevaClave2026", "NuevaClave2026");
        Assert.Equal("/panel/central", correcto.Headers.Location?.OriginalString);
        Assert.False((await CuentaAsync(operador.Email!)).DebeCambiarContrasena);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/panel/central")).StatusCode); // la sesión nueva ya opera
        Assert.Equal("/panel/central", (await fabrica.CrearNavegador().IngresarAlPanelAsync(operador.Email!, "NuevaClave2026")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SerenoConContrasenaProvisional_LaApiLoRechaza_HastaCambiarlaDesdeLaApp()
    {
        var admin = await AdministradorAsync();
        var sereno = await CrearPorPanelAsync(admin, Roles.Sereno);
        var token = await (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token",
            new SolicitudInicioSesion(sereno.Email!, "ClaveInicial2026"))).Content.ReadFromJsonAsync<RespuestaToken>();
        Assert.True(token!.DebeCambiarContrasena);
        var app = Cliente(token.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await app.PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);

        var invalido = await app.PostAsJsonAsync("/api/cuentas/contrasena", new SolicitudCambioContrasena("ClaveInicial2026", "corta", "corta"));
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);

        var cambio = await app.PostAsJsonAsync("/api/cuentas/contrasena", new SolicitudCambioContrasena("ClaveInicial2026", "NuevaClave2026", "NuevaClave2026"));
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var nuevo = await cambio.Content.ReadFromJsonAsync<RespuestaToken>();
        Assert.False(nuevo!.DebeCambiarContrasena);

        // El token anterior ya no vale (el sello cambió); el nuevo opera.
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/api/cuentas/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(nuevo.Token).PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(false, false))).StatusCode);
    }

    [Fact]
    public async Task ElCambioDeContrasenaPropio_NoEsParaElCiudadano()
    {
        var ciudadano = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

        var respuesta = await ciudadano.PostAsJsonAsync("/api/cuentas/contrasena", new SolicitudCambioContrasena("a", "NuevaClave2026", "NuevaClave2026"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task UnaContrasenaRestablecidaPorElAdministrador_VuelveASerProvisional()
    {
        var admin = await AdministradorAsync();
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        Assert.False((await CuentaAsync(cuenta.Correo)).DebeCambiarContrasena);

        var respuesta = await EnviarAsync(admin, RutaEditar(cuenta.Id), RutaEditar(cuenta.Id), new()
        {
            ["Formulario.Nombres"] = "Prueba", ["Formulario.Apellidos"] = "Operador", ["Formulario.Correo"] = cuenta.Correo,
            ["Formulario.Dni"] = DniUnico(), ["Formulario.NuevaContrasena"] = "Restablecida2026",
        });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.True((await CuentaAsync(cuenta.Correo)).DebeCambiarContrasena);
        Assert.Equal(RutaCambio, (await fabrica.CrearNavegador().IngresarAlPanelAsync(cuenta.Correo, "Restablecida2026")).Headers.Location?.OriginalString);
    }

    private HttpClient Cliente(string token)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    private static string FilaDe(string html, Guid id) =>
        Regex.Match(html, $"<tr data-cuenta=\"{id}\">.*?</tr>", RegexOptions.Singleline).Value;
}
