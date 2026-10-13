using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Cuentas;

// HU01 — Registrar mi cuenta de ciudadano (S1-T05, S1-T06). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class RegistroCiudadanoTests(FabricaCad fabrica)
{
    private const string Ruta = "/api/cuentas/registro";
    private const string ContrasenaValida = "ClaveSegura2026";

    private static string CorreoUnico() => $"ciudadano.{Guid.NewGuid():N}@correo.test";

    private static SolicitudRegistroCiudadano SolicitudValida(string? correo = null) =>
        new("Rosa María", "Quispe Huamán", correo ?? CorreoUnico(), "987654321", ContrasenaValida);

    private Task<int> ContarUsuariosAsync() => fabrica.ConBaseDatosAsync(db => db.Users.CountAsync());

    private Task<Usuario?> BuscarAsync(string correo) => fabrica.ConBaseDatosAsync(db =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == correo.Trim().ToUpperInvariant()));

    [Fact]
    public async Task CA01_DatosValidos_CreaCuentaActivaConRolCiudadano()
    {
        var solicitud = SolicitudValida();

        var respuesta = await fabrica.CreateClient().PostAsJsonAsync(Ruta, solicitud);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaRegistroCiudadano>();
        var usuario = await BuscarAsync(solicitud.Correo);
        Assert.NotNull(usuario);
        Assert.Equal(cuerpo!.Id, usuario.Id);
        Assert.True(usuario.Activo);
        Assert.Equal(solicitud.Correo, usuario.Email);
        Assert.Equal("987654321", usuario.PhoneNumber);
        var roles = await fabrica.ConServiciosAsync(s => s.GetRequiredService<UserManager<Usuario>>().GetRolesAsync(usuario));
        Assert.Equal(new[] { Roles.Ciudadano }, roles);
    }

    [Theory]
    [InlineData("nombres")]
    [InlineData("apellidos")]
    [InlineData("correo")]
    [InlineData("telefono")]
    [InlineData("contrasena")]
    public async Task CA02_DatoObligatorioVacio_RechazaIndicandoMotivoYNoCreaCuenta(string campo)
    {
        var s = SolicitudValida();
        var solicitud = campo switch
        {
            "nombres" => s with { Nombres = "  " },
            "apellidos" => s with { Apellidos = "" },
            "correo" => s with { Correo = "" },
            "telefono" => s with { Telefono = " " },
            _ => s with { Contrasena = "" },
        };
        var antes = await ContarUsuariosAsync();

        var respuesta = await fabrica.CreateClient().PostAsJsonAsync(Ruta, solicitud);

        await AfirmarRechazoAsync(respuesta, campo, "obligatori");
        Assert.Equal(antes, await ContarUsuariosAsync());
    }

    [Fact]
    public async Task CA02_CampoAusenteEnLaSolicitud_RechazaYNoCreaCuenta()
    {
        var antes = await ContarUsuariosAsync();

        var respuesta = await fabrica.CreateClient().PostAsJsonAsync(Ruta,
            new { nombres = "Rosa", apellidos = "Quispe", correo = CorreoUnico(), contrasena = ContrasenaValida });

        await AfirmarRechazoAsync(respuesta, "telefono", "obligatori");
        Assert.Equal(antes, await ContarUsuariosAsync());
    }

    [Theory]
    [InlineData("correo", "no-es-un-correo")]
    [InlineData("correo", "rosa@dominio-sin-punto")]
    [InlineData("telefono", "12345")]
    [InlineData("telefono", "812345678")]
    [InlineData("telefono", "98765432a")]
    [InlineData("contrasena", "corta1")]
    [InlineData("contrasena", "sinnumeroslarga")]
    [InlineData("contrasena", "1234567890")]
    [InlineData("nombres", "N")] // se sustituye por 101 caracteres
    public async Task CA03_DatoInvalido_RechazaIndicandoMotivoYNoCreaCuenta(string campo, string valor)
    {
        var s = SolicitudValida();
        var solicitud = campo switch
        {
            "correo" => s with { Correo = valor },
            "telefono" => s with { Telefono = valor },
            "contrasena" => s with { Contrasena = valor },
            _ => s with { Nombres = new string('N', 101) },
        };
        var antes = await ContarUsuariosAsync();

        var respuesta = await fabrica.CreateClient().PostAsJsonAsync(Ruta, solicitud);

        await AfirmarRechazoAsync(respuesta, campo, "");
        Assert.Equal(antes, await ContarUsuariosAsync());
    }

    [Fact]
    public async Task CA04_CorreoYaRegistrado_RechazaYNoModificaLaCuentaExistente()
    {
        var cliente = fabrica.CreateClient();
        var original = SolicitudValida();
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync(Ruta, original)).StatusCode);
        var antes = await BuscarAsync(original.Correo);

        // Mismo correo con otra capitalización y espacios: es el mismo identificador.
        var repetida = new SolicitudRegistroCiudadano("Otro", "Nombre",
            $"  {original.Correo.ToUpperInvariant()} ", "912345678", "OtraClave99");
        var respuesta = await cliente.PostAsJsonAsync(Ruta, repetida);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<ProblemaJson>();
        Assert.Contains("Ya existe una cuenta", problema!.Title);
        var despues = await BuscarAsync(original.Correo);
        Assert.Equal(antes!.Nombres, despues!.Nombres);
        Assert.Equal(antes.PhoneNumber, despues.PhoneNumber);
        Assert.Equal(antes.PasswordHash, despues.PasswordHash);
    }

    [Fact]
    public async Task CA05_RegistrosSimultaneosConElMismoCorreo_CreaUnaSolaCuenta()
    {
        const int solicitudes = 10;
        var correo = CorreoUnico();
        var cliente = fabrica.CreateClient();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, solicitudes)
            .Select(_ => cliente.PostAsJsonAsync(Ruta, SolicitudValida(correo))));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(solicitudes - 1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var cuentas = await fabrica.ConBaseDatosAsync(db =>
            db.Users.CountAsync(u => u.NormalizedEmail == correo.ToUpperInvariant()));
        Assert.Equal(1, cuentas);
        var rolesAsignados = await fabrica.ConBaseDatosAsync(db =>
            db.UserRoles.CountAsync(ur => db.Users.Any(u => u.Id == ur.UserId && u.NormalizedEmail == correo.ToUpperInvariant())));
        Assert.Equal(1, rolesAsignados);
    }

    // La prueba HTTP anterior suele resolverse en la comprobación previa del caso de uso.
    // Esta verifica la garantía de fondo: aunque dos solicitudes superen esa comprobación a la vez,
    // la base de datos rechaza la segunda cuenta con el mismo correo (y RegistrarCiudadano lo traduce a 409).
    [Fact]
    public async Task CA05_IndiceUnico_BaseDeDatosRechazaSegundaCuentaConElMismoCorreo()
    {
        var correo = CorreoUnico();
        Usuario NuevaCuenta() => new()
        {
            Id = Guid.CreateVersion7(),
            UserName = correo,
            NormalizedUserName = correo.ToUpperInvariant(),
            Email = correo,
            NormalizedEmail = correo.ToUpperInvariant(),
            Nombres = "Rosa",
            Apellidos = "Quispe",
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        await fabrica.ConBaseDatosAsync(async db => { db.Users.Add(NuevaCuenta()); return await db.SaveChangesAsync(); });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            fabrica.ConBaseDatosAsync(async db => { db.Users.Add(NuevaCuenta()); return await db.SaveChangesAsync(); }));

        var postgres = Assert.IsType<Npgsql.PostgresException>(error.InnerException);
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    [Fact]
    public async Task CA06_Contrasena_SeGuardaConHashPbkdf2YNoApareceEnBaseDeDatosNiRegistros()
    {
        var solicitud = SolicitudValida() with { Contrasena = "Contrasena-Unica-7319" };

        var respuesta = await fabrica.CreateClient().PostAsJsonAsync(Ruta, solicitud);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var usuario = await BuscarAsync(solicitud.Correo);
        var hash = usuario!.PasswordHash!;
        Assert.DoesNotContain(solicitud.Contrasena, hash);

        // Formato V3 de ASP.NET Core Identity: 0x01 | PRF | iteraciones | long. sal | sal | subclave.
        var bytes = Convert.FromBase64String(hash);
        Assert.Equal(0x01, bytes[0]);
        Assert.Equal((uint)KeyDerivationPrfSha512, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)));
        Assert.Equal(100_000u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5)));
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9)));
        Assert.Equal(13 + 16 + 32, bytes.Length);

        // Ninguna columna de la cuenta contiene la contraseña en claro.
        var columnas = await fabrica.ConBaseDatosAsync(db => db.Database
            .SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM "AspNetUsers" u WHERE u::text LIKE {"%" + solicitud.Contrasena + "%"}""")
            .SingleAsync());
        Assert.Equal(0, columnas);

        // Ni la contraseña ni los datos personales aparecen en los registros del servidor.
        Assert.NotEmpty(fabrica.Registros.Entradas);
        foreach (var dato in new[] { solicitud.Contrasena, solicitud.Correo, solicitud.Telefono })
            Assert.DoesNotContain(fabrica.Registros.Entradas, entrada => entrada.Contains(dato, StringComparison.OrdinalIgnoreCase));
    }

    // KeyDerivationPrf.HMACSHA512 en Microsoft.AspNetCore.Cryptography.KeyDerivation.
    private const int KeyDerivationPrfSha512 = 2;

    private static async Task AfirmarRechazoAsync(HttpResponseMessage respuesta, string campo, string fragmentoMotivo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problema);
        Assert.True(problema.Errors.TryGetValue(campo, out var motivos), $"Falta el motivo para «{campo}».");
        Assert.Contains(motivos, m => m.Contains(fragmentoMotivo, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProblemaJson(string Title, int Status);
}
