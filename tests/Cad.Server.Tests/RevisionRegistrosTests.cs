using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cad.Contratos.Cuentas;
using Cad.Contratos.Reportes;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var dniOperador = Random.Shared.Next(10_000_000, 100_000_000).ToString(CultureInfo.InvariantCulture);
        sensibles["datos personales"].Add(correoOperador);
        sensibles["datos personales"].Add(dniOperador); // el DNI del personal tampoco se registra
        await EnviarAsync(admin, "/panel/admin/cuentas/nueva", new()
        {
            ["Formulario.Nombres"] = "Carlos", ["Formulario.Apellidos"] = "Mendoza", ["Formulario.Correo"] = correoOperador, ["Formulario.Dni"] = dniOperador,
            ["Formulario.Rol"] = Roles.Operador, ["Formulario.Contrasena"] = claveInicial,
        });
        var operadorId = await fabrica.ConBaseDatosAsync(db => db.Users.Where(u => u.Email == correoOperador).Select(u => u.Id).SingleAsync());
        await EnviarAsync(admin, $"/panel/admin/cuentas/{operadorId}/editar", new()
        {
            ["Formulario.Nombres"] = "Carlos", ["Formulario.Apellidos"] = "Mendoza", ["Formulario.Correo"] = correoOperador, ["Formulario.Dni"] = dniOperador,
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
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsJsonAsync($"/api/sereno/casos/{sos.Id}/atencion", await fabrica.EnElLugarAsync(sos.Id))).StatusCode);
        var resultadoCierre = $"Se atendió a la señora del domicilio {sello}";
        var notasCierre = $"Vecino identificado como agresor {sello}";
        sensibles["datos personales"].AddRange([resultadoCierre, notasCierre]);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsJsonAsync($"/api/sereno/casos/{sos.Id}/cierre",
            new Cad.Contratos.Serenos.SolicitudCierreCaso(Cad.Server.Modulos.Casos.Dominio.CategoriaIncidencia.Iniciales[0].Id, resultadoCierre, notasCierre))).StatusCode);
        Assert.Contains(notasCierre, await central.Navegador.GetStringAsync($"/panel/central/sos/{sos.Id}"));

        // Sellos de seguridad de Identity (equivalen a credenciales para revocar sesiones).
        sensibles["tokens y cookies"].AddRange(await fabrica.ConBaseDatosAsync(db => db.Users
            .Where(u => u.Email == correoCiudadana || u.Email == correoOperador).Select(u => u.SecurityStamp!).ToListAsync()));

        AfirmarSinDatosSensibles(sensibles);
    }

    // S3-T17 (Sprint 3, HU10–HU31): reportes con su descripción, la ubicación del hecho por punto, por posición elegida y por
    // referencia; revisión, corrección de categoría, admisión, derivación (destino, acción y motivo) y descarte (motivo);
    // asignación del admitido y su cierre por el sereno; búsqueda e historial en el panel; «Mis casos» y la bandeja
    // de notificaciones del ciudadano. Las descripciones, referencias, motivos, destinos y acciones pueden contener
    // datos personales y lugares concretos.
    [Fact]
    public async Task S3T17_FlujoDeReportesYSeguimiento_NingunDatoSensibleEnLosRegistros()
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

        var ciudadana = await fabrica.CrearAsync(Roles.Ciudadano);
        sensibles["contraseñas"].Add(ciudadana.Clave);
        sensibles["datos personales"].Add(ciudadana.Correo);
        var app = await fabrica.CrearClienteAppAsync(ciudadana);
        sensibles["tokens y cookies"].Add(app.DefaultRequestHeaders.Authorization!.Parameter!);

        // HU10: tres reportes, uno por cada fuente de la ubicación del hecho.
        Coordenadas(sensibles, out var latMapa, out var lonMapa);
        Coordenadas(sensibles, out var latActual, out var lonActual);
        var descripciones = new[] { $"Robaron la moto del vecino {sello}", $"Hombre armado en el parque {sello}", $"Venta de drogas en la esquina {sello}" };
        var referencias = new[] { $"Al costado del mercado {sello}", $"Junto al colegio {sello}", $"Pasaje sin nombre {sello}" };
        sensibles["datos personales"].AddRange([.. descripciones, .. referencias]);
        var lugares = new[]
        {
            new UbicacionHecho(FuentesUbicacionHecho.Mapa, latMapa, lonMapa, null, referencias[0]),
            new UbicacionHecho(FuentesUbicacionHecho.PosicionActual, latActual, lonActual, 8, referencias[1]),
            new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, referencias[2]),
        };
        var reportes = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var respuesta = await app.PostAsJsonAsync("/api/reportes", new SolicitudRegistroReporte(Guid.NewGuid(),
                CategoriaIncidencia.Iniciales[1].Id, descripciones[i], DateTimeOffset.UtcNow.AddMinutes(-30), lugares[i]));
            Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
            reportes.Add((await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id);
        }

        // HU14: el operador ve la bandeja, inicia la revisión y corrige la categoría de cada uno.
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var panel = fabrica.CrearNavegador();
        var ingreso = await panel.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        sensibles["contraseñas"].Add(operador.Clave);
        sensibles["tokens y cookies"].Add(ValorCookie(ingreso));
        Assert.Contains(reportes[0].ToString(), await panel.GetStringAsync("/panel/reportes"));
        foreach (var id in reportes)
        {
            Assert.Contains(descripciones[reportes.IndexOf(id)], System.Net.WebUtility.HtmlDecode(await panel.GetStringAsync($"/panel/reportes/{id}")));
            await EnviarAsync(panel, $"/panel/reportes/{id}?handler=IniciarRevision", [], $"/panel/reportes/{id}");
            await EnviarAsync(panel, $"/panel/reportes/{id}?handler=Corregir",
                new() { ["CategoriaId"] = CategoriaIncidencia.Iniciales[3].Id.ToString() }, $"/panel/reportes/{id}");
        }

        // HU15–HU17: admite el primero, deriva el segundo y descarta el tercero, con motivos, destino y acción.
        var motivos = new[] { $"Riesgo para la familia del denunciante {sello}", $"Competencia de la comisaría {sello}", $"Duplicado del vecino {sello}" };
        var destino = $"Comisaría de Lince, suboficial {sello}";
        var accion = $"Llamada al 105 con el DNI del denunciante {sello}";
        sensibles["datos personales"].AddRange([.. motivos, destino, accion]);
        await EnviarAsync(panel, $"/panel/reportes/{reportes[0]}?handler=Decidir",
            new() { ["Decision.Tipo"] = "Admision", ["Decision.Motivo"] = motivos[0] }, $"/panel/reportes/{reportes[0]}");
        await EnviarAsync(panel, $"/panel/reportes/{reportes[1]}?handler=Decidir", new()
        {
            ["Decision.Tipo"] = "Derivacion", ["Decision.Motivo"] = motivos[1], ["Decision.Destino"] = destino, ["Decision.Accion"] = accion,
        }, $"/panel/reportes/{reportes[1]}");
        await EnviarAsync(panel, $"/panel/reportes/{reportes[2]}?handler=Decidir",
            new() { ["Decision.Tipo"] = "Descarte", ["Decision.Motivo"] = motivos[2] }, $"/panel/reportes/{reportes[2]}");

        // HU20-CA07: el admitido se asigna desde la central a un sereno operativo, que lo atiende y lo cierra (HU21–HU24).
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var serenoApp = await fabrica.CrearClienteAppAsync(sereno);
        sensibles["contraseñas"].Add(sereno.Clave);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PutAsJsonAsync("/api/sereno/estado",
            new Cad.Contratos.Serenos.SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        Assert.Contains(reportes[0].ToString(), await panel.GetStringAsync("/panel/central"));
        await EnviarAsync(panel, $"/panel/reportes/{reportes[0]}?handler=Asignar", new()
        {
            ["Asignacion.SerenoId"] = sereno.Id.ToString(), ["Asignacion.IdSolicitud"] = Guid.NewGuid().ToString(), ["Asignacion.Volver"] = "central",
        }, "/panel/central");
        Assert.Contains(referencias[0], await serenoApp.GetStringAsync("/api/sereno/despacho"));
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsync($"/api/sereno/casos/{reportes[0]}/desplazamiento", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsJsonAsync($"/api/sereno/casos/{reportes[0]}/atencion", await fabrica.EnElLugarAsync(reportes[0]))).StatusCode);
        var resultado = $"Se recuperó la moto del señor {sello}";
        var notas = $"El denunciante es primo del sospechoso {sello}";
        sensibles["datos personales"].AddRange([resultado, notas]);
        Assert.Equal(HttpStatusCode.OK, (await serenoApp.PostAsJsonAsync($"/api/sereno/casos/{reportes[0]}/cierre",
            new Cad.Contratos.Serenos.SolicitudCierreCaso(CategoriaIncidencia.Iniciales[1].Id, resultado, notas))).StatusCode);

        // HU26 y HU27: búsqueda con filtros e historial con notas internas.
        Assert.Contains(reportes[1].ToString(), await panel.GetStringAsync("/panel/casos?Origen=Reporte"));
        foreach (var id in reportes)
            Assert.Equal(HttpStatusCode.OK, (await panel.GetAsync($"/panel/casos/{id}/historial")).StatusCode);

        // HU30 y HU31: la ciudadana consulta sus casos y su bandeja, y marca una notificación como leída.
        Assert.Contains(destino, await app.GetStringAsync("/api/casos/mios"));
        Assert.Contains(resultado, await app.GetStringAsync($"/api/casos/mios/{reportes[0]}"));
        var bandeja = (await app.GetFromJsonAsync<Cad.Contratos.Casos.BandejaNotificaciones>("/api/notificaciones"))!;
        Assert.NotEmpty(bandeja.Notificaciones);
        Assert.Equal(HttpStatusCode.NoContent, (await app.PostAsync($"/api/notificaciones/{bandeja.Notificaciones[0].Id}/leida", null)).StatusCode);

        AfirmarSinDatosSensibles(sensibles);
    }

    // S4-T18 (Sprint 4: HU05, HU25, HU34–HU39): acompañamiento con posiciones (también un reenvío y una posición inválida),
    // finalización, supervisión en el panel (activos, recorrido, historial), conservación excepcional con su finalidad,
    // eliminación por plazo, mapa de casos y desactivación de una cuenta con su asignación cancelada. Las posiciones del
    // acompañamiento y la finalidad de la conservación son datos sensibles.
    [Fact]
    public async Task S4T18_AcompanamientoSupervisionYDesactivacion_NingunDatoSensibleEnLosRegistros()
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
        var ciudadana = await fabrica.CrearAsync(Roles.Ciudadano, telefono: $"9{Random.Shared.Next(10_000_000, 99_999_999)}");
        sensibles["contraseñas"].Add(ciudadana.Clave);
        sensibles["datos personales"].Add(ciudadana.Correo);
        var app = await fabrica.CrearClienteAppAsync(ciudadana);
        sensibles["tokens y cookies"].Add(app.DefaultRequestHeaders.Authorization!.Parameter!);

        // HU34–HU37: una sesión con posiciones precisas, un reenvío, una posición inválida y su finalización.
        var sesion = (await (await app.PostAsJsonAsync("/api/acompanamiento",
            new Cad.Contratos.Acompanamiento.SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<Cad.Contratos.Acompanamiento.SesionAcompanamientoVista>())!;
        var obtenida = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            Coordenadas(sensibles, out var lat, out var lon);
            await app.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones",
                new Cad.Contratos.Acompanamiento.PosicionAcompanamientoEnviada(lat, lon, 7.25 + i, obtenida.AddSeconds(-15 * i)));
        }
        Coordenadas(sensibles, out var latInvalida, out _);
        await app.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones",
            new Cad.Contratos.Acompanamiento.PosicionAcompanamientoEnviada(latInvalida, 500, 5, obtenida));
        await app.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones",
            new Cad.Contratos.Acompanamiento.PosicionAcompanamientoEnviada(-12.0, -77.0, 5, obtenida)); // reenvío (misma hora)
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsync($"/api/acompanamiento/{sesion.Id}/finalizacion", null)).StatusCode);

        // HU38 y HU39-A: el operador consulta activos, recorrido e historial; HU39-B: conserva el recorrido con una finalidad.
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var panel = fabrica.CrearNavegador();
        var ingreso = await panel.IngresarAlPanelAsync(operador.Correo, operador.Clave);
        sensibles["contraseñas"].Add(operador.Clave);
        sensibles["tokens y cookies"].Add(ValorCookie(ingreso));
        await panel.GetStringAsync("/panel/acompanamientos");
        await panel.GetStringAsync("/panel/acompanamientos?handler=Lista");
        await panel.GetStringAsync("/panel/acompanamientos/historial");
        var recorrido = await panel.GetStringAsync($"/panel/acompanamientos/{sesion.Id}");
        sensibles["datos personales"].Add(ciudadana.Correo);
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        var finalidad = $"Investigación por denuncia del vecino {sello}";
        sensibles["datos personales"].Add(finalidad);
        await EnviarAsync(panel, $"/panel/acompanamientos/{sesion.Id}?handler=Conservar",
            new() { ["Conservacion.CasoId"] = sos.ToString(), ["Conservacion.Finalidad"] = finalidad }, $"/panel/acompanamientos/{sesion.Id}");
        Assert.Contains(sesion.Id.ToString(), recorrido);

        // HU39-B: una sesión vencida hace más de 30 días pierde sus posiciones (la tarea solo registra cuántas).
        var vieja = (await (await app.PostAsJsonAsync("/api/acompanamiento",
            new Cad.Contratos.Acompanamiento.SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<Cad.Contratos.Acompanamiento.SesionAcompanamientoVista>())!;
        Coordenadas(sensibles, out var latVieja, out var lonVieja);
        await app.PostAsJsonAsync($"/api/acompanamiento/{vieja.Id}/posiciones",
            new Cad.Contratos.Acompanamiento.PosicionAcompanamientoEnviada(latVieja, lonVieja, 6, DateTimeOffset.UtcNow));
        await app.PostAsync($"/api/acompanamiento/{vieja.Id}/finalizacion", null);
        await fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"SesionesAcompanamiento\" SET \"IniciadaEn\" = \"IniciadaEn\" - interval '40 days', \"VenceEn\" = \"VenceEn\" - interval '40 days', \"CerradaEn\" = \"CerradaEn\" - interval '40 days' WHERE \"Id\" = {vieja.Id}"));
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<Cad.Server.Modulos.Acompanamiento.Aplicacion.ConservarRecorridos>().EliminarVencidasAsync(CancellationToken.None));

        // HU25: mapa de casos; HU05 (DP03): desactivación de un sereno con una asignación vigente.
        await panel.GetStringAsync("/panel/central?handler=CasosMapa");
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        sensibles["contraseñas"].Add(sereno.Clave);
        sensibles["datos personales"].Add(sereno.Correo);
        var serenoApp = await fabrica.CrearClienteAppAsync(sereno);
        await serenoApp.PutAsJsonAsync("/api/sereno/estado", new Cad.Contratos.Serenos.SolicitudCambioEstadoOperativo(true, true));
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<Cad.Server.Modulos.Casos.Aplicacion.AsignarCaso>()
            .EjecutarAsync(sos, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None));
        var admin = fabrica.CrearNavegador();
        sensibles["tokens y cookies"].Add(ValorCookie(await admin.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave)));
        await EnviarAsync(admin, $"/panel/admin/cuentas/{sereno.Id}/editar?handler=Desactivar", [], $"/panel/admin/cuentas/{sereno.Id}/editar");
        Assert.Equal(HttpStatusCode.Unauthorized, (await serenoApp.GetAsync("/api/sereno/estado")).StatusCode);

        sensibles["datos personales"].Add(await fabrica.ConBaseDatosAsync(db => db.Users.Where(u => u.Id == ciudadana.Id).Select(u => u.PhoneNumber!).SingleAsync()));
        sensibles["tokens y cookies"].AddRange(await fabrica.ConBaseDatosAsync(db => db.Users
            .Where(u => u.Id == ciudadana.Id || u.Id == sereno.Id).Select(u => u.SecurityStamp!).ToListAsync()));
        AfirmarSinDatosSensibles(sensibles);
    }

    [Fact]
    public async Task S5T16_CancelacionEvaluacionBienestarAvisosYMapas_NingunDatoSensibleEnLosRegistros()
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
        var ciudadana = await fabrica.CrearAsync(Roles.Ciudadano, telefono: $"9{Random.Shared.Next(10_000_000, 99_999_999)}");
        sensibles["contraseñas"].Add(ciudadana.Clave);
        sensibles["datos personales"].Add(ciudadana.Correo);
        var app = await fabrica.CrearClienteAppAsync(ciudadana);
        sensibles["tokens y cookies"].Add(app.DefaultRequestHeaders.Authorization!.Parameter!);
        var operador = await fabrica.CrearAsync(Roles.Operador);
        sensibles["contraseñas"].Add(operador.Clave);
        var panel = fabrica.CrearNavegador();
        sensibles["tokens y cookies"].Add(ValorCookie(await panel.IngresarAlPanelAsync(operador.Correo, operador.Clave)));

        // HU09: un SOS con ubicación cancelado con un motivo personal.
        Coordenadas(sensibles, out var latSos, out var lonSos);
        var ahora = DateTimeOffset.UtcNow;
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(latSos, lonSos, 9, ahora.AddSeconds(1)))))
            .Content.ReadFromJsonAsync<RespuestaSos>())!.Id;
        var motivo = $"Mi hermano {sello} ya llegó a casa";
        sensibles["datos personales"].Add(motivo);
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync($"/api/casos/mios/{sos}/cancelacion", new Cad.Contratos.Casos.SolicitudCancelacionSos(motivo))).StatusCode);

        // HU40/HU41: acompañamiento con posiciones, confirmación presentada y «Necesito ayuda» con la ubicación actual.
        var sesion = (await (await app.PostAsJsonAsync("/api/acompanamiento", new Cad.Contratos.Acompanamiento.SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<Cad.Contratos.Acompanamiento.SesionAcompanamientoVista>())!;
        Coordenadas(sensibles, out var latPos, out var lonPos);
        await app.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones",
            new Cad.Contratos.Acompanamiento.PosicionAcompanamientoEnviada(latPos, lonPos, 6, DateTimeOffset.UtcNow));
        await EnviarAsync(panel, $"/panel/acompanamientos/{sesion.Id}?handler=SolicitarConfirmacion",
            new() { ["IdSolicitudConfirmacion"] = Guid.NewGuid().ToString() }, $"/panel/acompanamientos/{sesion.Id}");
        var confirmacion = (await app.GetFromJsonAsync<Cad.Contratos.Acompanamiento.ConfirmacionBienestarVista>($"/api/acompanamiento/{sesion.Id}/confirmacion"))!;
        await app.PostAsync($"/api/acompanamiento/confirmaciones/{confirmacion.Id}/presentacion", null);
        Coordenadas(sensibles, out var latAyuda, out var lonAyuda);
        ahora = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.OK, (await app.PostAsJsonAsync($"/api/acompanamiento/confirmaciones/{confirmacion.Id}/respuesta",
            new Cad.Contratos.Acompanamiento.SolicitudRespuestaBienestar(Cad.Contratos.Acompanamiento.RespuestasBienestar.NecesitoAyuda,
                new SolicitudEmisionSos(Guid.NewGuid(), ahora, new UbicacionSos(latAyuda, lonAyuda, 8, ahora.AddSeconds(1)))))).StatusCode);

        // HU42: avisos del servidor; HU43: revisión, actuación con datos personales y caso urgente con justificación.
        await fabrica.ConServiciosAsync(s => s.GetRequiredService<Cad.Server.Modulos.Acompanamiento.Aplicacion.EvaluarAvisos>()
            .EjecutarAsync(DateTimeOffset.UtcNow.AddMinutes(3), CancellationToken.None));
        var aviso = await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.Where(a => a.SesionId == sesion.Id).Select(a => a.Id).FirstAsync());
        await EnviarAsync(panel, $"/panel/avisos/{aviso}?handler=Revisar", [], $"/panel/avisos/{aviso}");
        var actuacion = $"Llamé al {sello} y respondió su madre";
        var justificacion = $"Sin respuesta de {sello} tras la llamada";
        sensibles["datos personales"].AddRange([actuacion, justificacion]);
        await EnviarAsync(panel, $"/panel/avisos/{aviso}?handler=Actuacion",
            new() { ["TipoActuacion"] = "Contacto", ["Descripcion"] = actuacion, ["IdSolicitud"] = Guid.NewGuid().ToString() }, $"/panel/avisos/{aviso}");
        await EnviarAsync(panel, $"/panel/avisos/{aviso}?handler=CrearCaso",
            new() { ["Justificacion"] = justificacion, ["IdSolicitud"] = Guid.NewGuid().ToString() }, $"/panel/avisos/{aviso}");
        await EnviarAsync(panel, $"/panel/avisos/{aviso}?handler=Resolver", [], $"/panel/avisos/{aviso}");
        Assert.NotNull(await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.Where(a => a.Id == aviso).Select(a => a.CasoId).SingleAsync()));

        // HU32/HU33: evaluación con comentario de un caso cerrado; HU28 y HU29: búsqueda con tiempos y mapa de concentración.
        var cerrado = await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == sos).Select(c => c.Id).SingleAsync());
        await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.Id == cerrado).ExecuteUpdateAsync(c => c.SetProperty(x => x.Estado, Cad.Server.Modulos.Casos.Dominio.EstadoCaso.Cerrada)));
        var comentario = $"El sereno {sello} fue muy amable";
        sensibles["datos personales"].Add(comentario);
        await app.PostAsJsonAsync($"/api/casos/mios/{cerrado}/evaluacion", new Cad.Contratos.Casos.SolicitudEvaluacionCaso(Guid.NewGuid(), 5, comentario));
        await panel.GetStringAsync($"/panel/casos/{cerrado}/historial");
        await panel.GetStringAsync("/panel/casos");
        await panel.GetStringAsync("/panel/mapa-concentracion");

        sensibles["datos personales"].Add(await fabrica.ConBaseDatosAsync(db => db.Users.Where(u => u.Id == ciudadana.Id).Select(u => u.PhoneNumber!).SingleAsync()));
        sensibles["tokens y cookies"].Add(await fabrica.ConBaseDatosAsync(db => db.Users.Where(u => u.Id == ciudadana.Id).Select(u => u.SecurityStamp!).SingleAsync()));
        AfirmarSinDatosSensibles(sensibles);
    }

    private static void Coordenadas(Dictionary<string, List<string>> sensibles, out double latitud, out double longitud)
    {
        (latitud, longitud) = (-12.0 - Random.Shared.Next(100_000, 999_999) / 1e7, -77.0 - Random.Shared.Next(100_000, 999_999) / 1e7);
        sensibles["coordenadas"].AddRange([latitud.ToString(CultureInfo.InvariantCulture), longitud.ToString(CultureInfo.InvariantCulture),
            latitud.ToString(new CultureInfo("es-PE")), longitud.ToString(new CultureInfo("es-PE"))]);
    }

    // Ningún valor sensible en ninguna entrada capturada (de todas las pruebas de la colección), y ningún JWT con forma reconocible.
    private void AfirmarSinDatosSensibles(Dictionary<string, List<string>> sensibles)
    {
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

    // pagina: la página que contiene el formulario (por defecto, la misma ruta).
    private static async Task EnviarAsync(HttpClient navegador, string ruta, Dictionary<string, string> campos, string? pagina = null)
    {
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, pagina ?? ruta);
        Assert.Equal(HttpStatusCode.Redirect, (await navegador.PostAsync(ruta, new FormUrlEncodedContent(campos))).StatusCode);
    }

    private static string ValorCookie(HttpResponseMessage respuesta) =>
        respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("cad.panel=")).Split(';')[0]["cad.panel=".Length..];

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex Jwt();
}
