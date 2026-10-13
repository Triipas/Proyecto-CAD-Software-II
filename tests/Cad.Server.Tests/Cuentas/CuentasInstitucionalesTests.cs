using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Cuentas;

// HU04 — Crear, consultar y modificar cuentas institucionales (S1-T10, S1-T11). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class CuentasInstitucionalesTests(FabricaCad fabrica)
{
    private const string RutaListado = "/panel/admin/cuentas";
    private const string RutaNueva = "/panel/admin/cuentas/nueva";
    private static string RutaEditar(Guid id) => $"/panel/admin/cuentas/{id}/editar";

    private static string CorreoUnico(string prefijo = "personal") => $"{prefijo}.{Guid.NewGuid():N}@correo.test";

    private async Task<HttpClient> AdministradorAsync()
    {
        var navegador = fabrica.CrearNavegador();
        Assert.Equal(HttpStatusCode.Redirect, (await navegador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave)).StatusCode);
        return navegador;
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient navegador, string paginaConToken, string ruta, Dictionary<string, string> campos)
    {
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, paginaConToken);
        return await navegador.PostAsync(ruta, new FormUrlEncodedContent(campos));
    }

    private static Dictionary<string, string> DatosNuevos(string correo, string rol = Roles.Operador, string clave = "ClaveInicial2026") => new()
    {
        ["Formulario.Nombres"] = "Carlos",
        ["Formulario.Apellidos"] = "Mendoza Ríos",
        ["Formulario.Correo"] = correo,
        ["Formulario.Telefono"] = "912345678",
        ["Formulario.Rol"] = rol,
        ["Formulario.Contrasena"] = clave,
    };

    private Task<int> ContarUsuariosAsync() => fabrica.ConBaseDatosAsync(db => db.Users.CountAsync());

    private Task<Usuario?> BuscarAsync(string correo) => fabrica.ConBaseDatosAsync(db =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == correo.Trim().ToUpperInvariant()));

    private Task<IList<string>> RolesDeAsync(Usuario usuario) =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<UserManager<Usuario>>().GetRolesAsync(usuario));

    [Theory]
    [InlineData(Roles.Operador)]
    [InlineData(Roles.Sereno)]
    public async Task CA01_DatosValidos_CreaCuentaConElRolElegidoYUtilizable(string rol)
    {
        var admin = await AdministradorAsync();
        var correo = CorreoUnico(rol.ToLowerInvariant());

        var respuesta = await EnviarAsync(admin, RutaNueva, RutaNueva, DatosNuevos(correo, rol));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(RutaListado, respuesta.Headers.Location?.OriginalString);
        var usuario = await BuscarAsync(correo);
        Assert.NotNull(usuario);
        Assert.True(usuario.Activo);
        Assert.Equal(new[] { rol }, await RolesDeAsync(usuario));

        // S1-T10: la cuenta queda utilizable en su canal (HU02).
        if (rol == Roles.Operador)
            Assert.Equal("/panel/central", (await fabrica.CrearNavegador().IngresarAlPanelAsync(correo, "ClaveInicial2026")).Headers.Location?.OriginalString);
        else
            Assert.Equal(HttpStatusCode.OK, (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token", new SolicitudInicioSesion(correo, "ClaveInicial2026"))).StatusCode);

        // Tras crear, el listado confirma la operación.
        var listado = await admin.GetStringAsync(RutaListado);
        Assert.Contains("Se creó la cuenta", listado);
    }

    [Fact]
    public async Task CA02_CrearConCorreoExistente_RechazaYNoAlteraNingunaCuenta()
    {
        var existente = await fabrica.CrearAsync(Roles.Ciudadano);
        var antes = await BuscarAsync(existente.Correo);
        var total = await ContarUsuariosAsync();

        var respuesta = await EnviarAsync(await AdministradorAsync(), RutaNueva, RutaNueva,
            DatosNuevos($" {existente.Correo.ToUpperInvariant()} "));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("Ya existe una cuenta con ese correo electrónico.", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Equal(total, await ContarUsuariosAsync());
        var despues = await BuscarAsync(existente.Correo);
        Assert.Equal(antes!.Nombres, despues!.Nombres);
        Assert.Equal(antes.PasswordHash, despues.PasswordHash);
        Assert.Equal(new[] { Roles.Ciudadano }, await RolesDeAsync(despues));
    }

    [Fact]
    public async Task CA02_ModificarConCorreoDeOtraCuenta_RechazaYNoAlteraNingunaCuenta()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Sereno);
        var otra = await fabrica.CrearAsync(Roles.Operador);
        var admin = await AdministradorAsync();

        var respuesta = await EnviarAsync(admin, RutaEditar(cuenta.Id), RutaEditar(cuenta.Id), new()
        {
            ["Formulario.Nombres"] = "Cambiado",
            ["Formulario.Apellidos"] = "Cambiado",
            ["Formulario.Correo"] = otra.Correo,
            ["Formulario.Telefono"] = "",
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Ya existe otra cuenta con ese correo electrónico.", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("Prueba", (await BuscarAsync(cuenta.Correo))!.Nombres);
        Assert.NotNull(await BuscarAsync(otra.Correo));
    }

    [Theory]
    [InlineData("Formulario.Nombres", "", "Los nombres son obligatorios.")]
    [InlineData("Formulario.Correo", "no-es-un-correo", "El correo electrónico no tiene un formato válido.")]
    [InlineData("Formulario.Telefono", "12345", "El teléfono móvil debe tener 9 dígitos y empezar por 9.")]
    [InlineData("Formulario.Contrasena", "corta1", "La contraseña debe tener entre 8 y 128 caracteres")]
    [InlineData("Formulario.Rol", "", "Elige el rol: Operador o Sereno.")]
    [InlineData("Formulario.Rol", "Administrador", "Elige el rol: Operador o Sereno.")]
    [InlineData("Formulario.Rol", "Ciudadano", "Elige el rol: Operador o Sereno.")]
    public async Task CA03_DatoIncompletoOInvalido_NoCreaLaCuentaNiDejaRegistrosParciales(string campo, string valor, string motivo)
    {
        var correo = CorreoUnico();
        var datos = DatosNuevos(correo);
        datos[campo] = valor;
        var total = await ContarUsuariosAsync();
        var asignaciones = await fabrica.ConBaseDatosAsync(db => db.UserRoles.CountAsync());

        var respuesta = await EnviarAsync(await AdministradorAsync(), RutaNueva, RutaNueva, datos);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains(motivo, html);
        Assert.Contains("No se pudo guardar la cuenta", html);
        Assert.Equal(total, await ContarUsuariosAsync());
        Assert.Equal(asignaciones, await fabrica.ConBaseDatosAsync(db => db.UserRoles.CountAsync()));
    }

    [Fact]
    public async Task CA04_Listado_MuestraCadaCuentaInstitucionalConRolYEstado_YSoloInstitucionales()
    {
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sereno = await fabrica.CrearAsync(Roles.Sereno, activa: false);
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);

        var html = await (await AdministradorAsync()).GetStringAsync(RutaListado);

        var filaOperador = FilaDe(html, operador.Id);
        Assert.Contains(operador.Correo, filaOperador);
        Assert.Contains("<td>Operador</td>", filaOperador);
        Assert.Contains("Activa", filaOperador);
        var filaSereno = FilaDe(html, sereno.Id);
        Assert.Contains("<td>Sereno</td>", filaSereno);
        Assert.Contains("Desactivada", filaSereno);
        Assert.DoesNotContain(ciudadano.Correo, html);
        Assert.DoesNotContain(fabrica.AdminCorreo, html);
    }

    [Fact]
    public async Task CA05_Modificacion_GuardaLosDatosPermitidosSinDuplicarIdentificadores()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var nuevoCorreo = CorreoUnico("renombrado");
        var total = await ContarUsuariosAsync();

        var respuesta = await EnviarAsync(await AdministradorAsync(), RutaEditar(cuenta.Id), RutaEditar(cuenta.Id), new()
        {
            ["Formulario.Nombres"] = "Lucía",
            ["Formulario.Apellidos"] = "Paredes Soto",
            ["Formulario.Correo"] = nuevoCorreo,
            ["Formulario.Telefono"] = "955111222",
            ["Formulario.NuevaContrasena"] = "",
        });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Null(await BuscarAsync(cuenta.Correo));
        var modificada = await BuscarAsync(nuevoCorreo);
        Assert.Equal(cuenta.Id, modificada!.Id);
        Assert.Equal("Lucía", modificada.Nombres);
        Assert.Equal("955111222", modificada.PhoneNumber);
        Assert.Equal(new[] { Roles.Operador }, await RolesDeAsync(modificada)); // el rol no cambia
        Assert.Equal(total, await ContarUsuariosAsync());
        // La contraseña se mantiene y el nuevo correo es el identificador de acceso.
        Assert.Equal(HttpStatusCode.Redirect, (await fabrica.CrearNavegador().IngresarAlPanelAsync(nuevoCorreo, cuenta.Clave)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Ciudadano)]
    [InlineData(Roles.Administrador)]
    public async Task CA05_SoloCuentasInstitucionales_OtrasCuentasNoSePuedenModificar(string rol)
    {
        var otra = rol == Roles.Administrador
            ? new CuentaDePrueba((await BuscarAsync(fabrica.AdminCorreo))!.Id, fabrica.AdminCorreo, fabrica.AdminClave, rol)
            : await fabrica.CrearAsync(rol);
        var admin = await AdministradorAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(RutaEditar(otra.Id))).StatusCode);
        var respuesta = await EnviarAsync(admin, RutaListado, RutaEditar(otra.Id), new()
        {
            ["Formulario.Nombres"] = "Cambiado",
            ["Formulario.Apellidos"] = "Cambiado",
            ["Formulario.Correo"] = otra.Correo,
        });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.NotEqual("Cambiado", (await BuscarAsync(otra.Correo))!.Nombres);
    }

    public static TheoryData<string> PerfilesNoAdministradores => new() { "Anónimo", Roles.Ciudadano, Roles.Operador, Roles.Sereno };

    [Theory]
    [MemberData(nameof(PerfilesNoAdministradores))]
    public async Task CA06_SolicitudDirectaDeOtroPerfil_SeRechazaYNoModificaDatos(string perfil)
    {
        var objetivo = await fabrica.CrearAsync(Roles.Sereno);
        var correoNuevo = CorreoUnico("intruso");
        var (cliente, token) = await ClienteDeAsync(perfil); // crea la cuenta del propio perfil
        var total = await ContarUsuariosAsync();

        async Task<HttpResponseMessage> Post(string ruta, Dictionary<string, string> campos)
        {
            if (token is not null) campos["__RequestVerificationToken"] = token;
            return await cliente.PostAsync(ruta, new FormUrlEncodedContent(campos));
        }

        var listado = await cliente.GetAsync(RutaListado);
        var alta = await Post(RutaNueva, DatosNuevos(correoNuevo));
        var modificacion = await Post(RutaEditar(objetivo.Id), new()
        {
            ["Formulario.Nombres"] = "Intruso",
            ["Formulario.Apellidos"] = "Intruso",
            ["Formulario.Correo"] = objetivo.Correo,
            ["Formulario.NuevaContrasena"] = "OtraClave2026",
        });

        foreach (var respuesta in new[] { listado, alta, modificacion })
        {
            var destino = respuesta.Headers.Location?.ToString() ?? "";
            Assert.True(respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || (respuesta.StatusCode == HttpStatusCode.Redirect && (destino.Contains("/panel/ingresar") || destino.Contains("/panel/acceso-denegado"))),
                $"{perfil}: se esperaba un rechazo y se obtuvo {(int)respuesta.StatusCode} {destino}");
            Assert.DoesNotContain(objetivo.Correo, await respuesta.Content.ReadAsStringAsync()); // sin datos restringidos
        }

        // HU03-CA03: ningún dato cambió.
        Assert.Equal(total, await ContarUsuariosAsync());
        Assert.Null(await BuscarAsync(correoNuevo));
        var intacta = await BuscarAsync(objetivo.Correo);
        Assert.Equal("Prueba", intacta!.Nombres);
        Assert.Equal(HttpStatusCode.OK, (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token",
            new SolicitudInicioSesion(objetivo.Correo, objetivo.Clave))).StatusCode); // la contraseña no cambió
    }

    [Fact]
    public async Task CA07_ContrasenaCreadaYModificada_SoloComoHashEnBaseDeDatosYNuncaEnRegistros()
    {
        var admin = await AdministradorAsync();
        var correo = CorreoUnico();
        const string inicial = "Inicial-Secreta-4821";
        const string nueva = "Nueva-Secreta-9374";

        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(admin, RutaNueva, RutaNueva, DatosNuevos(correo, Roles.Operador, inicial))).StatusCode);
        var creada = await BuscarAsync(correo);
        AfirmarHashV3(creada!.PasswordHash!, inicial);

        var modificacion = await EnviarAsync(admin, RutaEditar(creada.Id), RutaEditar(creada.Id), new()
        {
            ["Formulario.Nombres"] = creada.Nombres,
            ["Formulario.Apellidos"] = creada.Apellidos,
            ["Formulario.Correo"] = correo,
            ["Formulario.NuevaContrasena"] = nueva,
        });
        Assert.Equal(HttpStatusCode.Redirect, modificacion.StatusCode);
        var modificada = await BuscarAsync(correo);
        AfirmarHashV3(modificada!.PasswordHash!, nueva);
        Assert.NotEqual(creada.PasswordHash, modificada.PasswordHash);

        // La nueva contraseña funciona y la anterior ya no.
        Assert.Equal(HttpStatusCode.Redirect, (await fabrica.CrearNavegador().IngresarAlPanelAsync(correo, nueva)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fabrica.CrearNavegador().IngresarAlPanelAsync(correo, inicial)).StatusCode);

        var filas = await fabrica.ConBaseDatosAsync(db => db.Database
            .SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM "AspNetUsers" u WHERE u::text LIKE {"%" + inicial + "%"} OR u::text LIKE {"%" + nueva + "%"}""")
            .SingleAsync());
        Assert.Equal(0, filas);
        foreach (var dato in new[] { inicial, nueva, correo })
            Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(dato, StringComparison.OrdinalIgnoreCase));
    }

    private static void AfirmarHashV3(string hash, string contrasena)
    {
        Assert.DoesNotContain(contrasena, hash);
        var bytes = Convert.FromBase64String(hash);
        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)));        // HMAC-SHA512
        Assert.Equal(100_000u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5)));  // iteraciones
    }

    private static string FilaDe(string html, Guid id)
    {
        var inicio = html.IndexOf($"data-cuenta=\"{id}\"", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"No aparece la fila de la cuenta {id}.");
        return html[inicio..html.IndexOf("</tr>", inicio, StringComparison.Ordinal)];
    }

    private async Task<(HttpClient Cliente, string? Token)> ClienteDeAsync(string perfil)
    {
        var navegador = fabrica.CrearNavegador();
        if (perfil == "Anónimo")
            return (navegador, null);

        var cuenta = await fabrica.CrearAsync(perfil);
        if (perfil is Roles.Ciudadano or Roles.Sereno)
        {
            var token = await (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token",
                new SolicitudInicioSesion(cuenta.Correo, cuenta.Clave))).Content.ReadFromJsonAsync<RespuestaToken>();
            navegador.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
            return (navegador, null);
        }

        // Operador: sesión de panel válida y token antifalsificación propio, para que el rechazo
        // se deba al rol y no a un token ausente.
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return (navegador, await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/central"));
    }
}
