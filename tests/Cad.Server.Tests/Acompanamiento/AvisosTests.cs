using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Acompanamiento;
using Cad.Contratos.Casos;
using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Acompanamiento.Aplicacion;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Acompanamiento;

// HU42 — Avisos técnicos y de verificación (S5-T07) y HU43 — su revisión y atención (S5-T08, S5-T09; DP19, decisión del
// equipo). Las reglas se evalúan con un «ahora» explícito, posterior a los datos de la prueba. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class AvisosTests(FabricaCad fabrica)
{
    private sealed record Sesion(HttpClient App, CuentaDePrueba Cuenta, Guid Id, DateTimeOffset IniciadaEn);

    private async Task<Sesion> AcompanamientoAsync()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Ciudadano);
        var app = await fabrica.CrearClienteAppAsync(cuenta);
        var vista = (await (await app.PostAsJsonAsync("/api/acompanamiento", new SolicitudInicioAcompanamiento(Guid.NewGuid(), true, true)))
            .Content.ReadFromJsonAsync<SesionAcompanamientoVista>())!;
        return new(app, cuenta, vista.Id, vista.IniciadaEn);
    }

    private static async Task<DateTimeOffset> PosicionAsync(Sesion sesion, double latitud = -12.0464)
    {
        var obtenida = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Created, (await sesion.App.PostAsJsonAsync($"/api/acompanamiento/{sesion.Id}/posiciones",
            new PosicionAcompanamientoEnviada(latitud, -77.0428, 7, obtenida))).StatusCode);
        return obtenida;
    }

    private Task<IReadOnlyList<AvisoAcompanamiento>> EvaluarAsync(DateTimeOffset ahora) =>
        fabrica.ConServiciosAsync(s => s.GetRequiredService<EvaluarAvisos>().EjecutarAsync(ahora, CancellationToken.None));

    private Task<List<AvisoAcompanamiento>> AvisosAsync(Guid sesionId) =>
        fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().Where(a => a.SesionId == sesionId).OrderBy(a => a.GeneradoEn).ToListAsync());

    // Un aviso de ubicación desactualizada sobre una sesión sin posiciones.
    private async Task<(Sesion Sesion, Guid AvisoId)> AvisoAsync()
    {
        var sesion = await AcompanamientoAsync();
        await EvaluarAsync(sesion.IniciadaEn.AddSeconds(61));
        return (sesion, Assert.Single(await AvisosAsync(sesion.Id)).Id);
    }

    private Task<T> ServicioAsync<T>(Func<AtenderAvisos, Task<T>> accion) =>
        fabrica.ConServiciosAsync(s => accion(s.GetRequiredService<AtenderAvisos>()));

    private async Task<HttpClient> PanelAsync(string rol = Roles.Operador)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient navegador, Guid avisoId, string manejador, Dictionary<string, string>? campos = null)
    {
        var ruta = $"/panel/avisos/{avisoId}";
        campos ??= [];
        campos["__RequestVerificationToken"] = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, ruta);
        return await navegador.PostAsync($"{ruta}?handler={manejador}", new FormUrlEncodedContent(campos));
    }

    // ----- HU42 -----

    [Fact]
    public async Task HU42_CA01_MasDe60SegundosSinPosiciones_GeneraUnAvisoConReglaFechaHoraYDatosDeOrigen()
    {
        var sesion = await AcompanamientoAsync();
        await EvaluarAsync(sesion.IniciadaEn.AddSeconds(60)); // 60 s justos: aún no
        Assert.Empty(await AvisosAsync(sesion.Id));

        var ahora = sesion.IniciadaEn.AddSeconds(61);
        await EvaluarAsync(ahora);

        var aviso = Assert.Single(await AvisosAsync(sesion.Id));
        Assert.Equal((ReglaAviso.UbicacionDesactualizada, EstadoAviso.Generado), (aviso.Regla, aviso.Estado));
        Assert.Equal(ahora.ToUnixTimeMilliseconds(), aviso.GeneradoEn.ToUnixTimeMilliseconds());
        Assert.Equal(sesion.IniciadaEn.ToUnixTimeMilliseconds(), aviso.ReferenciaEn.ToUnixTimeMilliseconds()); // sin posiciones: desde el inicio
        Assert.Equal(sesion.Cuenta.Id, aviso.CiudadanoId);
    }

    [Fact]
    public async Task HU42_CA01_ConPosiciones_LaReferenciaEsLaUltimaPosicion()
    {
        var sesion = await AcompanamientoAsync();
        var obtenida = await PosicionAsync(sesion);

        await EvaluarAsync(obtenida.AddSeconds(59));
        Assert.Empty(await AvisosAsync(sesion.Id));
        await EvaluarAsync(obtenida.AddSeconds(61));

        var aviso = Assert.Single(await AvisosAsync(sesion.Id));
        Assert.Equal(obtenida.ToUnixTimeMilliseconds(), aviso.ReferenciaEn.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task HU42_CA01_UnaPosicionRecienteEnUnaSesionAntigua_NoGeneraAviso()
    {
        var sesion = await AcompanamientoAsync();
        // La sesión empezó hace 5 minutos; la primera posición llega ahora.
        await fabrica.ConBaseDatosAsync(db => db.SesionesAcompanamiento.Where(s => s.Id == sesion.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.IniciadaEn, sesion.IniciadaEn.AddMinutes(-5)).SetProperty(x => x.VenceEn, sesion.IniciadaEn.AddMinutes(25))));
        var obtenida = await PosicionAsync(sesion);

        await EvaluarAsync(obtenida.AddSeconds(30));

        Assert.Empty(await AvisosAsync(sesion.Id));
    }

    [Fact]
    public void HU43_CA01_CA03_EnElDominio_UnAvisoEnRevisionNoCambiaDeResponsableYSoloSeResuelveEnRevision()
    {
        var ahora = DateTimeOffset.UtcNow;
        var sesion = SesionAcompanamiento.Iniciar(Guid.NewGuid(), Guid.NewGuid(), ahora);
        var aviso = AvisoAcompanamiento.UbicacionDesactualizada(sesion, null, ahora.AddSeconds(61));
        Guid primero = Guid.NewGuid(), segundo = Guid.NewGuid();

        Assert.False(aviso.Resolver(primero, ahora));
        Assert.True(aviso.IniciarRevision(primero, ahora));
        Assert.False(aviso.IniciarRevision(segundo, ahora));
        Assert.Equal(primero, aviso.ResponsableId);
        Assert.True(aviso.Resolver(segundo, ahora));
        Assert.False(aviso.IniciarRevision(segundo, ahora));
        Assert.Equal((EstadoAviso.Resuelto, (Guid?)primero, (Guid?)segundo), (aviso.Estado, aviso.ResponsableId, aviso.ResueltoPor));
    }

    [Fact]
    public async Task HU42_CA02_ConfirmacionPresentadaSinRespuestaDosMinutos_GeneraUnAvisoDeVerificacion()
    {
        var sesion = await AcompanamientoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var confirmacion = Assert.IsType<ResultadoSolicitudBienestar.Solicitada>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<ConfirmarBienestar>().SolicitarAsync(sesion.Id, operador.Id, Guid.NewGuid(), CancellationToken.None))).Confirmacion;
        var presentada = (await (await sesion.App.PostAsync($"/api/acompanamiento/confirmaciones/{confirmacion.Id}/presentacion", null))
            .Content.ReadFromJsonAsync<ConfirmacionBienestarVista>())!.PresentadaEn!.Value;
        await PosicionAsync(sesion); // con posiciones al día, solo cuenta la confirmación

        await EvaluarAsync(presentada.AddSeconds(55));
        Assert.DoesNotContain(await AvisosAsync(sesion.Id), a => a.Regla == ReglaAviso.ConfirmacionSinRespuesta);
        await EvaluarAsync(presentada.AddSeconds(121));

        var aviso = Assert.Single(await AvisosAsync(sesion.Id), a => a.Regla == ReglaAviso.ConfirmacionSinRespuesta);
        Assert.Equal(confirmacion.Id, aviso.ConfirmacionId);
        Assert.Equal(presentada.ToUnixTimeMilliseconds(), aviso.ReferenciaEn.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task HU42_CA02_SinPresentarORespondida_NoGeneraAvisoDeVerificacion()
    {
        var sinPresentar = await AcompanamientoAsync();
        var respondida = await AcompanamientoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        async Task<Guid> PedirAsync(Guid sesion) => Assert.IsType<ResultadoSolicitudBienestar.Solicitada>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<ConfirmarBienestar>().SolicitarAsync(sesion, operador.Id, Guid.NewGuid(), CancellationToken.None))).Confirmacion.Id;
        await PedirAsync(sinPresentar.Id);
        var id = await PedirAsync(respondida.Id);
        Assert.Equal(HttpStatusCode.OK, (await respondida.App.PostAsJsonAsync($"/api/acompanamiento/confirmaciones/{id}/respuesta",
            new SolicitudRespuestaBienestar(RespuestasBienestar.EstoyBien, null))).StatusCode);

        await EvaluarAsync(DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.DoesNotContain(await AvisosAsync(sinPresentar.Id), a => a.Regla == ReglaAviso.ConfirmacionSinRespuesta);
        Assert.DoesNotContain(await AvisosAsync(respondida.Id), a => a.Regla == ReglaAviso.ConfirmacionSinRespuesta);
    }

    [Fact]
    public async Task HU42_CA03_LaCondicionQuePersiste_NoRepiteElAviso_YUnaNuevaSi()
    {
        var sesion = await AcompanamientoAsync();
        var primera = await PosicionAsync(sesion);
        await EvaluarAsync(primera.AddSeconds(61));
        await EvaluarAsync(primera.AddSeconds(120));
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => EvaluarAsync(primera.AddSeconds(150))));
        Assert.Single(await AvisosAsync(sesion.Id));

        // Llega una posición nueva y vuelve a quedar desactualizada: es otra condición.
        var segunda = await PosicionAsync(sesion, -12.05);
        await EvaluarAsync(segunda.AddSeconds(61));
        var avisos = await AvisosAsync(sesion.Id);
        Assert.Equal(2, avisos.Count);
        Assert.Equal(segunda.ToUnixTimeMilliseconds(), avisos[1].ReferenciaEn.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task HU42_CA03_LaBaseDeDatosTampocoAdmiteDosAvisosDeLaMismaCondicion()
    {
        var (sesion, _) = await AvisoAsync();

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => fabrica.ConBaseDatosAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"AvisosAcompanamiento\" (\"Id\", \"SesionId\", \"CiudadanoId\", \"Regla\", \"Clave\", \"GeneradoEn\", \"ReferenciaEn\", \"Estado\", \"CasoCreado\") VALUES ({Guid.NewGuid()}, {sesion.Id}, {sesion.Cuenta.Id}, {"UbicacionDesactualizada"}, {"sin-posiciones"}, now(), now(), {"Generado"}, false)")));
        Assert.Equal("23505", error.SqlState);
    }

    [Fact]
    public async Task HU42_CA04_UnAviso_NoEsUnSosNiGeneraDespacho()
    {
        var casos = await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync());
        var asignaciones = await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync());
        var (sesion, avisoId) = await AvisoAsync();
        var operador = await PanelAsync();

        Assert.DoesNotContain(avisoId.ToString(), await operador.GetStringAsync("/panel/central?handler=Lista"));
        Assert.Empty(await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.CiudadanoId == sesion.Cuenta.Id).ToListAsync()));
        Assert.Equal(casos, await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync()));
        Assert.Equal(asignaciones, await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync()));
        // Se presenta en «Acompañamientos», como aviso técnico.
        var lista = System.Net.WebUtility.HtmlDecode(await operador.GetStringAsync("/panel/acompanamientos?handler=Lista"));
        Assert.Contains($"data-aviso=\"{avisoId}\"", lista);
        Assert.Contains("Aviso técnico", lista);
    }

    [Fact]
    public async Task HU42_CA05_CA06_SesionFinalizadaOVencida_NoSeEvaluaNiGeneraAvisos()
    {
        var finalizada = await AcompanamientoAsync();
        Assert.Equal(HttpStatusCode.OK, (await finalizada.App.PostAsync($"/api/acompanamiento/{finalizada.Id}/finalizacion", null)).StatusCode);
        var vencida = await AcompanamientoAsync();

        // Pasa el tiempo: la vencida ya superó sus 30 minutos (aunque el temporizador aún no la haya cerrado).
        await EvaluarAsync(vencida.IniciadaEn.AddMinutes(31));

        Assert.Empty(await AvisosAsync(finalizada.Id));
        Assert.Empty(await AvisosAsync(vencida.Id));
    }

    // ----- HU43 -----

    [Fact]
    public async Task HU43_CA01_AlRevisarlo_PasaAEnRevisionConSuResponsable()
    {
        var (_, avisoId) = await AvisoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(operador.Correo, operador.Clave);

        var respuesta = await EnviarAsync(navegador, avisoId, "Revisar");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var aviso = await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().SingleAsync(a => a.Id == avisoId));
        Assert.Equal((EstadoAviso.EnRevision, (Guid?)operador.Id), (aviso.Estado, aviso.ResponsableId));
        Assert.NotNull(aviso.RevisionIniciadaEn);
        Assert.Contains("En revisión · responsable: Prueba Operador", System.Net.WebUtility.HtmlDecode(await navegador.GetStringAsync($"/panel/avisos/{avisoId}")));
        // Otro operador no se lo quita: sigue con su responsable.
        var otro = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoAvisoAtendido.Correcto>(await ServicioAsync(s => s.RevisarAsync(avisoId, otro.Id, CancellationToken.None)));
        Assert.Equal(operador.Id, (await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().SingleAsync(a => a.Id == avisoId))).ResponsableId);
    }

    [Fact]
    public async Task HU43_CA02_ContactosAccionesYResultados_SeConservanConResponsableFechaYHora()
    {
        var (_, avisoId) = await AvisoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        // Sin revisar, no se registran actuaciones.
        Assert.IsType<ResultadoAvisoAtendido.Rechazado>(await ServicioAsync(s =>
            s.RegistrarActuacionAsync(avisoId, operador.Id, "Contacto", "Llamada", Guid.NewGuid(), CancellationToken.None)));
        await ServicioAsync(s => s.RevisarAsync(avisoId, operador.Id, CancellationToken.None));
        var reenviada = Guid.NewGuid();

        foreach (var (tipo, texto, id) in new[] { ("Contacto", "Llamada al ciudadano: contestó", reenviada), ("Accion", "Se le pidió reactivar la ubicación", Guid.NewGuid()),
                     ("Resultado", "El ciudadano está bien", Guid.NewGuid()), ("Contacto", "Llamada al ciudadano: contestó", reenviada) })
            Assert.IsType<ResultadoAvisoAtendido.Correcto>(await ServicioAsync(s =>
                s.RegistrarActuacionAsync(avisoId, operador.Id, tipo, texto, id, CancellationToken.None)));
        Assert.IsType<ResultadoAvisoAtendido.Invalido>(await ServicioAsync(s =>
            s.RegistrarActuacionAsync(avisoId, operador.Id, "Otro", " ", Guid.NewGuid(), CancellationToken.None)));

        var actuaciones = await fabrica.ConBaseDatosAsync(db => db.ActuacionesAviso.AsNoTracking().Where(a => a.AvisoId == avisoId).OrderBy(a => a.RegistradaEn).ToListAsync());
        Assert.Equal([TipoActuacionAviso.Contacto, TipoActuacionAviso.Accion, TipoActuacionAviso.Resultado], actuaciones.Select(a => a.Tipo));
        Assert.All(actuaciones, a => Assert.Equal(operador.Id, a.OperadorId));
        var html = System.Net.WebUtility.HtmlDecode(await (await PanelAsync()).GetStringAsync($"/panel/avisos/{avisoId}"));
        Assert.Contains("Se le pidió reactivar la ubicación", html);
        Assert.Contains("Actuaciones · 3", html);
    }

    [Fact]
    public async Task HU43_CA03_AlResolverlo_PasaAResueltoConservandoResponsableYActuaciones()
    {
        var (_, avisoId) = await AvisoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        Assert.IsType<ResultadoAvisoAtendido.Rechazado>(await ServicioAsync(s => s.ResolverAsync(avisoId, operador.Id, CancellationToken.None)));
        await ServicioAsync(s => s.RevisarAsync(avisoId, operador.Id, CancellationToken.None));
        await ServicioAsync(s => s.RegistrarActuacionAsync(avisoId, operador.Id, "Resultado", "Sin novedad", Guid.NewGuid(), CancellationToken.None));

        Assert.IsType<ResultadoAvisoAtendido.Correcto>(await ServicioAsync(s => s.ResolverAsync(avisoId, operador.Id, CancellationToken.None)));

        var aviso = await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().SingleAsync(a => a.Id == avisoId));
        Assert.Equal((EstadoAviso.Resuelto, (Guid?)operador.Id, (Guid?)operador.Id), (aviso.Estado, aviso.ResponsableId, aviso.ResueltoPor));
        Assert.NotNull(aviso.ResueltoEn);
        Assert.Single(await fabrica.ConBaseDatosAsync(db => db.ActuacionesAviso.Where(a => a.AvisoId == avisoId).ToListAsync()));
        Assert.IsType<ResultadoAvisoAtendido.Rechazado>(await ServicioAsync(s =>
            s.RegistrarActuacionAsync(avisoId, operador.Id, "Accion", "Tarde", Guid.NewGuid(), CancellationToken.None)));
        // Resuelto, deja la lista de avisos abiertos.
        Assert.DoesNotContain($"data-aviso=\"{avisoId}\"", await (await PanelAsync()).GetStringAsync("/panel/acompanamientos?handler=Lista"));
    }

    [Fact]
    public async Task HU43_CA04_VincularConUnCasoExistente()
    {
        var (sesion, avisoId) = await AvisoAsync();
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var sos = (await (await sesion.App.PostAsJsonAsync("/api/sos", new Cad.Contratos.Sos.SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<Cad.Contratos.Sos.RespuestaSos>())!.Id;

        Assert.IsType<ResultadoAvisoAtendido.Invalido>(await ServicioAsync(s => s.VincularAsync(avisoId, Guid.NewGuid(), operador.Id, CancellationToken.None)));
        Assert.IsType<ResultadoAvisoAtendido.Correcto>(await ServicioAsync(s => s.VincularAsync(avisoId, sos, operador.Id, CancellationToken.None)));
        Assert.IsType<ResultadoAvisoAtendido.Correcto>(await ServicioAsync(s => s.VincularAsync(avisoId, sos, operador.Id, CancellationToken.None))); // reenvío
        var otroCaso = (await (await sesion.App.PostAsJsonAsync("/api/sos", new Cad.Contratos.Sos.SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<Cad.Contratos.Sos.RespuestaSos>())!.Id;
        Assert.IsType<ResultadoAvisoAtendido.Rechazado>(await ServicioAsync(s => s.VincularAsync(avisoId, otroCaso, operador.Id, CancellationToken.None)));

        var aviso = await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().SingleAsync(a => a.Id == avisoId));
        Assert.Equal((sos, false, (Guid?)operador.Id), (aviso.CasoId!.Value, aviso.CasoCreado, aviso.VinculadoPor));
    }

    [Fact]
    public async Task HU43_CA05_SinJustificacion_NoSeCreaElCasoUrgente()
    {
        var (sesion, avisoId) = await AvisoAsync();
        var navegador = await PanelAsync();

        var respuesta = await EnviarAsync(navegador, avisoId, "CrearCaso", new() { ["Justificacion"] = "   ", ["IdSolicitud"] = Guid.NewGuid().ToString() });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Indica la justificación del caso urgente.", System.Net.WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        Assert.Empty(await fabrica.ConBaseDatosAsync(db => db.Casos.Where(c => c.CiudadanoId == sesion.Cuenta.Id).ToListAsync()));
        Assert.Null((await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().SingleAsync(a => a.Id == avisoId))).CasoId);
    }

    [Fact]
    public async Task HU43_CA05_CA06_DP19_ConJustificacion_CreaElCasoUrgenteVinculadoYPendienteSinDespacho()
    {
        var sesion = await AcompanamientoAsync();
        var obtenida = await PosicionAsync(sesion, -12.0611);
        await EvaluarAsync(obtenida.AddSeconds(61));
        var avisoId = Assert.Single(await AvisosAsync(sesion.Id)).Id;
        var operador = await fabrica.CrearAsync(Roles.Operador);
        var idSolicitud = Guid.NewGuid();
        var asignaciones = await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync());

        var resultados = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ServicioAsync(s =>
            s.CrearCasoUrgenteAsync(avisoId, "El ciudadano no responde y su ubicación lleva minutos sin actualizarse.", operador.Id, idSolicitud, CancellationToken.None))));

        Assert.All(resultados, r => Assert.IsType<ResultadoAvisoAtendido.Correcto>(r));
        var caso = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().Where(c => c.CiudadanoId == sesion.Cuenta.Id).ToListAsync()));
        Assert.Equal((OrigenCaso.Acompanamiento, EstadoCaso.Pendiente), (caso.Origen, caso.Estado));
        Assert.Equal(-12.0611, caso.Latitud);
        Assert.Equal(obtenida.ToUnixTimeMilliseconds(), caso.UbicacionObtenidaEn!.Value.ToUnixTimeMilliseconds()); // la última conocida, con su hora real
        var aviso = await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().SingleAsync(a => a.Id == avisoId));
        Assert.Equal((caso.Id, true), (aviso.CasoId!.Value, aviso.CasoCreado));
        Assert.Equal("El ciudadano no responde y su ubicación lleva minutos sin actualizarse.", aviso.JustificacionCaso);
        // CA06: nada asignado; aparece pendiente en la central, como caso urgente, y se asigna con HU20.
        Assert.Equal(asignaciones, await fabrica.ConBaseDatosAsync(db => db.Asignaciones.CountAsync()));
        var navegador = await PanelAsync();
        var central = System.Net.WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/central?handler=Lista"));
        Assert.Contains($"data-sos=\"{caso.Id}\"", central);
        Assert.Contains("Caso urgente · Acompañamiento", central);
        var historial = System.Net.WebUtility.HtmlDecode(await navegador.GetStringAsync($"/panel/casos/{caso.Id}/historial"));
        Assert.Contains("Caso urgente creado desde un aviso del acompañamiento", historial);
        Assert.Contains("Justificación: El ciudadano no responde", historial);
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        Assert.Equal(HttpStatusCode.OK, (await (await fabrica.CrearClienteAppAsync(sereno)).PutAsJsonAsync("/api/sereno/estado", new SolicitudCambioEstadoOperativo(true, true))).StatusCode);
        Assert.IsType<ResultadoAsignacion.Asignado>(await fabrica.ConServiciosAsync(s =>
            s.GetRequiredService<AsignarCaso>().EjecutarAsync(caso.Id, sereno.Id, operador.Id, Guid.NewGuid(), CancellationToken.None)));
        // El titular lo ve en «Mis casos».
        var mios = (await sesion.App.GetFromJsonAsync<List<MiCasoResumen>>("/api/casos/mios"))!;
        Assert.Equal("Acompañamiento", Assert.Single(mios).Origen);
    }

    [Fact]
    public async Task HU43_UnAdministrador_NoAtiendeAvisos()
    {
        var (_, avisoId) = await AvisoAsync();
        var navegador = await PanelAsync(Roles.Administrador);
        var token = await CuentasDePrueba.ObtenerTokenAntifalsificacionAsync(navegador, "/panel/admin/cuentas");

        var respuesta = await navegador.PostAsync($"/panel/avisos/{avisoId}?handler=Revisar",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Contains("acceso-denegado", respuesta.Headers.Location?.ToString() ?? "");
        Assert.Equal(EstadoAviso.Generado, (await fabrica.ConBaseDatosAsync(db => db.AvisosAcompanamiento.AsNoTracking().SingleAsync(a => a.Id == avisoId))).Estado);
    }
}
