using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cad.Server.Tests;

// S1-T20 (transversal HU01–HU11), ampliada en el Sprint 2 (S2-T16, HU12–HU24): revisión de los registros. Recorre el flujo completo y
// busca cada dato sensible en TODAS las entradas capturadas (todos los niveles, incluidos Debug y Trace),
// lo que es más estricto que la configuración real (Microsoft.AspNetCore y SQL solo a partir de Warning).
// RNF01: sin contraseñas. RNF07: sin coordenadas. Plan de Arquitectura 9.7: sin tokens, cookies,
// cadenas de conexión ni datos personales. Ver docs/revision-registros.md.
[Collection(ColeccionServidor.Nombre)]
public partial class RevisionRegistrosTests(FabricaCad fabrica)
{
    [Fact]
    public async Task S1T20_FlujoCompletoDelSprint_NingunDatoSensibleEnLosRegistros()
    {
        var sello = Guid.NewGuid().ToString("N")[..10];
        var sensibles = new Dictionary<string, List<string>>
        {
            ["contraseñas"] = [],
            ["tokens y cookies"] = [],
            ["datos personales"] = [],
            ["coordenadas"] = [],
            ["secretos del servidor"] = [fabrica.ClaveFirmaJwt, fabrica.AdminClave, new NpgsqlConnectionStringBuilder(ConexionPruebas.Obtener()).Password!],
        };

        // HU01: registro de una ciudadana (datos ficticios únicos de esta prueba).
        var correoCiudadana = $"auditoria.{sello}@correo.test";
        var claveCiudadana = $"ClaveAuditoria{sello}1";
        var telefono = $"9{Random.Shared.Next(10_000_000, 99_999_999)}";
        var apellidos = $"Auditoria{sello}";
        sensibles["contraseñas"].Add(claveCiudadana);
        sensibles["datos personales"].AddRange([correoCiudadana, telefono, apellidos]);
        Assert.Equal(HttpStatusCode.Created, (await fabrica.CreateClient().PostAsJsonAsync("/api/cuentas/registro",
            new SolicitudRegistroCiudadano("Rosa", apellidos, correoCiudadana, telefono, claveCiudadana))).StatusCode);

        // HU02: inicio de sesión en la app (válido e inválido).
        var claveErronea = $"Erronea{sello}9";
        sensibles["contraseñas"].Add(claveErronea);
        await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token", new SolicitudInicioSesion(correoCiudadana, claveErronea));
        var token = (await (await fabrica.CreateClient().PostAsJsonAsync("/api/auth/token",
            new SolicitudInicioSesion(correoCiudadana, claveCiudadana))).Content.ReadFromJsonAsync<RespuestaToken>())!.Token;
        sensibles["tokens y cookies"].Add(token);
        var app = fabrica.CreateClient();
        app.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/api/cuentas/yo")).StatusCode);

        // HU02/HU04: el administrador entra al panel, crea un operador y le cambia la contraseña.
        var admin = fabrica.CrearNavegador();
        var ingresoAdmin = await admin.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);
        sensibles["tokens y cookies"].Add(ValorCookie(ingresoAdmin));
        var correoOperador = $"operador.auditoria.{sello}@correo.test";
        var claveInicial = $"Inicial{sello}2";
        var claveNueva = $"Nueva{sello}3";
        sensibles["contraseñas"].AddRange([claveInicial, claveNueva]);
        sensibles["datos personales"].Add(correoOperador);
        await EnviarAsync(admin, "/panel/admin/cuentas/nueva", new()
        {
            ["Formulario.Nombres"] = "Carlos", ["Formulario.Apellidos"] = "Mendoza", ["Formulario.Correo"] = correoOperador,
            ["Formulario.Rol"] = Roles.Operador, ["Formulario.Contrasena"] = claveInicial,
        });
        var operadorId = await fabrica.ConBaseDatosAsync(db => db.Users.Where(u => u.Email == correoOperador).Select(u => u.Id).SingleAsync());
        await EnviarAsync(admin, $"/panel/admin/cuentas/{operadorId}/editar", new()
        {
            ["Formulario.Nombres"] = "Carlos", ["Formulario.Apellidos"] = "Mendoza", ["Formulario.Correo"] = correoOperador,
            ["Formulario.NuevaContrasena"] = claveNueva,
        });

        // HU02 panel: ingreso fallido y correcto del operador.
        await fabrica.CrearNavegador().IngresarAlPanelAsync(correoOperador, claveInicial); // ya no es válida
        var ingresoOperador = await fabrica.CrearNavegador().IngresarAlPanelAsync(correoOperador, claveNueva);
        Assert.Equal(HttpStatusCode.Redirect, ingresoOperador.StatusCode);
        sensibles["tokens y cookies"].Add(ValorCookie(ingresoOperador));

        // HU07/HU08/HU11: SOS con ubicación precisa, recibido en la central en tiempo real.
        await using var central = await fabrica.OperadorEnCentralAsync();
        var (lat, lon) = (-12.0 - Random.Shared.Next(100_000, 999_999) / 1e7, -77.0 - Random.Shared.Next(100_000, 999_999) / 1e7);
        sensibles["coordenadas"].AddRange([lat.ToString(CultureInfo.InvariantCulture), lon.ToString(CultureInfo.InvariantCulture),
            lat.ToString(new CultureInfo("es-PE")), lon.ToString(new CultureInfo("es-PE"))]);
        var activado = DateTimeOffset.UtcNow;
        var sos = (await (await app.PostAsJsonAsync("/api/sos",
            new SolicitudEmisionSos(Guid.NewGuid(), activado, new UbicacionSos(lat, lon, 11, activado.AddSeconds(1)))))
            .Content.ReadFromJsonAsync<RespuestaSos>())!;
        Assert.True(await CentralDePrueba.EsperarAsync(() => central.Avisos.Contains(sos.Id)), "No llegó el aviso del SOS.");
        Assert.Contains(sos.Id.ToString(), await central.Navegador.GetStringAsync("/panel/central?handler=Lista"));

        // HU12 (Sprint 2): el operador ve el teléfono y registra un intento con información y referencia,
        // que pueden contener datos personales y lugares concretos.
        Assert.Contains(telefono, await central.Navegador.GetStringAsync($"/panel/central/sos/{sos.Id}"));
        var informacion = $"Informa pelea en el domicilio {sello}";
        var referencia = $"Frente a la bodega {sello}";
        sensibles["datos personales"].AddRange([informacion, referencia]);
        await EnviarAsync(central.Navegador, $"/panel/central/sos/{sos.Id}", new()
        {
            ["Formulario.IdSolicitud"] = Guid.NewGuid().ToString(), ["Formulario.Resultado"] = "Contactado",
            ["Formulario.InformacionHecho"] = informacion, ["Formulario.ReferenciaUbicacion"] = referencia,
        });

        // HU18 (Sprint 2): un sereno se activa y transmite posiciones con coordenadas precisas.
        var cuentaSereno = await fabrica.CrearAsync(Roles.Sereno);
        var serenoApp = await fabrica.CrearClienteAppAsync(cuentaSereno);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PutAsJsonAsync("/api/sereno/estado",
            new Cad.Contratos.Serenos.SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        var (latSereno, lonSereno) = (-12.0 - Random.Shared.Next(100_000, 999_999) / 1e7, -77.0 - Random.Shared.Next(100_000, 999_999) / 1e7);
        sensibles["coordenadas"].AddRange([latSereno.ToString(CultureInfo.InvariantCulture), lonSereno.ToString(CultureInfo.InvariantCulture),
            latSereno.ToString(new CultureInfo("es-PE")), lonSereno.ToString(new CultureInfo("es-PE"))]);
        Assert.Equal(HttpStatusCode.Created, (await serenoApp.PostAsJsonAsync("/api/sereno/posiciones",
            new Cad.Contratos.Serenos.PosicionSerenoEnviada(latSereno, lonSereno, 9, DateTimeOffset.UtcNow))).StatusCode);

        // HU21 (Sprint 2): el sereno registra el token de su dispositivo (ADR-07), recibe el despacho y lo consulta.
        var tokenDispositivo = $"token-dispositivo-{sello}";
        sensibles["tokens y cookies"].Add(tokenDispositivo);
        Assert.Equal(HttpStatusCode.NoContent, (await serenoApp.PutAsJsonAsync("/api/sereno/dispositivo",
            new Cad.Contratos.Serenos.SolicitudRegistroDispositivo(tokenDispositivo))).StatusCode);
        await EnviarAsync(central.Navegador, $"/panel/central/sos/{sos.Id}?handler=Asignar", new()
        {
            ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(), ["Asignacion.SerenoId"] = cuentaSereno.Id.ToString(),
        });
        Assert.True(await CentralDePrueba.EsperarAsync(() => fabrica.Push.Enviadas.Contains(tokenDispositivo)), "No se envió la push del despacho.");
        Assert.Contains(referencia, await serenoApp.GetStringAsync("/api/sereno/despacho"));

        // HU13 y HU22–HU24 (Sprint 2): el operador clasifica; el sereno va en camino, inicia la atención y cierra con
        // un resultado público y notas internas, que pueden contener datos personales.
        await EnviarAsync(central.Navegador, $"/panel/central/sos/{sos.Id}?handler=Clasificar", new()
        {
            ["Clasificacion.CategoriaId"] = Cad.Server.Modulos.Casos.Dominio.CategoriaIncidencia.Iniciales[0].Id.ToString(),
        });
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsync($"/api/sereno/casos/{sos.Id}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsync($"/api/sereno/casos/{sos.Id}/atencion", null)).StatusCode);
        var resultadoCierre = $"Se atendió a la señora del domicilio {sello}";
        var notasCierre = $"Vecino identificado como agresor {sello}";
        sensibles["datos personales"].AddRange([resultadoCierre, notasCierre]);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsJsonAsync($"/api/sereno/casos/{sos.Id}/cierre",
            new Cad.Contratos.Serenos.SolicitudCierreCaso(Cad.Server.Modulos.Casos.Dominio.CategoriaIncidencia.Iniciales[0].Id, resultadoCierre, notasCierre))).StatusCode);
        Assert.Contains(notasCierre, await central.Navegador.GetStringAsync($"/panel/central/sos/{sos.Id}"));

        // Sellos de seguridad de Identity (equivalen a credenciales para revocar sesiones).
        sensibles["tokens y cookies"].AddRange(await fabrica.ConBaseDatosAsync(db => db.Users
            .Where(u => u.Email == correoCiudadana || u.Email == correoOperador).Select(u => u.SecurityStamp!).ToListAsync()));

        // Revisión: ningún valor sensible en ninguna entrada, y ningún JWT con forma reconocible.
        var entradas = fabrica.Registros.Entradas.ToList();
        Assert.True(entradas.Count > 50, $"Muy pocas entradas capturadas ({entradas.Count}): la revisión no sería significativa.");
        var hallazgos = sensibles
            .SelectMany(c => c.Value.Where(v => !string.IsNullOrEmpty(v)).Select(v => (Categoria: c.Key, Valor: v)))
            .Where(s => entradas.Any(e => e.Contains(s.Valor, StringComparison.OrdinalIgnoreCase)))
            .Select(s => $"{s.Categoria}: «{s.Valor[..Math.Min(6, s.Valor.Length)]}…»")
            .ToList();
        hallazgos.AddRange(entradas.Where(e => Jwt().IsMatch(e)).Select(_ => "tokens y cookies: un JWT aparece en un registro").Distinct());
        hallazgos.AddRange(entradas.Where(e => e.Contains("Password=", StringComparison.OrdinalIgnoreCase)).Select(_ => "secretos del servidor: cadena de conexión con contraseña").Distinct());

        Assert.True(hallazgos.Count == 0, "Datos sensibles en los registros:\n" + string.Join("\n", hallazgos));
    }

    private static async Task EnviarAsync(HttpClient navegador, string ruta, Dictionary<string, string> campos)
    {
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, ruta);
        Assert.Equal(HttpStatusCode.Redirect, (await navegador.PostAsync(ruta, new FormUrlEncodedContent(campos))).StatusCode);
    }

    private static string ValorCookie(HttpResponseMessage respuesta) =>
        respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("cad.panel=")).Split(';')[0]["cad.panel=".Length..];

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex Jwt();
}
