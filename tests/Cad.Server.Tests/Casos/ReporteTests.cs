using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Cad.Contratos.Reportes;
using Cad.Contratos.Serenos;
using Cad.Contratos.Sos;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cad.Server.Tests.Casos;

// HU10 — Registrar un reporte de incidencia (parte de servidor de S3-T01 y S3-T02). Datos y coordenadas ficticios.
// La advertencia de peligro inmediato (CA05), el mapa (CA03) y la elección de la posición actual en el teléfono (CA04)
// pertenecen a la app (bloque 3).
[Collection(ColeccionServidor.Nombre)]
public class ReporteTests(FabricaCad fabrica)
{
    private const string Ruta = "/api/reportes";
    private static readonly Guid Robo = new("0199e2b0-0000-7000-8000-000000000002");
    private static readonly Guid Otros = new("0199e2b0-0000-7000-8000-000000000004");

    // En segundos enteros: PostgreSQL guarda microsegundos.
    private static DateTimeOffset HaceUnaHora =>
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()).ToOffset(TimeSpan.FromHours(-5));

    private static UbicacionHecho EnMapa(double lat = -12.0631, double lon = -77.0365, string? referencia = null) =>
        new(FuentesUbicacionHecho.Mapa, lat, lon, null, referencia);

    private static UbicacionHecho PorTexto(string referencia = "Esquina del mercado central") =>
        new(FuentesUbicacionHecho.Texto, null, null, null, referencia);

    private static SolicitudRegistroReporte Solicitud(UbicacionHecho? ubicacion = null, Guid? categoria = null, string? descripcion = "Robo de una bicicleta",
        DateTimeOffset? ocurrido = null) =>
        new(Guid.NewGuid(), categoria ?? Robo, descripcion, ocurrido ?? HaceUnaHora, ubicacion ?? EnMapa());

    private async Task<(HttpClient App, CuentaDePrueba Ciudadano)> CiudadanoAsync()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        return (await fabrica.CrearClienteAppAsync(ciudadano), ciudadano);
    }

    private Task<List<Caso>> CasosDeAsync(Guid ciudadanoId) =>
        fabrica.ConBaseDatosAsync(db => db.Casos.AsNoTracking().Where(c => c.CiudadanoId == ciudadanoId).ToListAsync());

    private Task<DetalleReporte?> DetalleAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.DetallesReporte.AsNoTracking().SingleOrDefaultAsync(d => d.CasoId == casoId));

    [Fact]
    public async Task CA01_RegistroValido_CreaUnCasoDeOrigenReporteEnEstadoRecibido()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var solicitud = Solicitud(EnMapa(-12.0631, -77.0365, "Frente a la panadería"));
        var antes = DateTimeOffset.UtcNow;

        var respuesta = await app.PostAsJsonAsync(Ruta, solicitud);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!;
        Assert.Equal("Recibido", cuerpo.Estado);
        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(cuerpo.Id, caso.Id);
        Assert.Equal(OrigenCaso.Reporte, caso.Origen);
        Assert.Equal(EstadoCaso.Recibido, caso.Estado);
        Assert.Equal(Robo, caso.CategoriaId);
        Assert.InRange(caso.RegistradoEn, antes.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var detalle = (await DetalleAsync(caso.Id))!;
        Assert.Equal("Robo de una bicicleta", detalle.Descripcion);
        Assert.Equal(solicitud.OcurridoAproximadamenteEn, detalle.OcurridoAproximadamenteEn);
        Assert.Equal("Frente a la panadería", detalle.ReferenciaHecho);
        // El historial nace en la misma transacción, visible para el titular.
        var evento = Assert.Single(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking().Where(e => e.CasoId == caso.Id).ToListAsync()));
        Assert.Equal(TipoEventoCaso.ReporteRegistrado, evento.Tipo);
        Assert.Equal(ciudadano.Id, evento.ActorId);
        Assert.Equal(VisibilidadEvento.Publica, evento.Visibilidad);
    }

    public static TheoryData<string, object> SinCampoObligatorio => new()
    {
        { "categoriaId", new { idSolicitud = Guid.NewGuid(), descripcion = "Hecho", ocurridoAproximadamenteEn = DateTimeOffset.UtcNow.AddHours(-1), ubicacion = PorTexto() } },
        { "descripcion", new { idSolicitud = Guid.NewGuid(), categoriaId = Robo, ocurridoAproximadamenteEn = DateTimeOffset.UtcNow.AddHours(-1), ubicacion = PorTexto() } },
        { "ocurridoAproximadamenteEn", new { idSolicitud = Guid.NewGuid(), categoriaId = Robo, descripcion = "Hecho", ubicacion = PorTexto() } },
        { "ubicacion", new { idSolicitud = Guid.NewGuid(), categoriaId = Robo, descripcion = "Hecho", ocurridoAproximadamenteEn = DateTimeOffset.UtcNow.AddHours(-1) } },
        { "idSolicitud", new { categoriaId = Robo, descripcion = "Hecho", ocurridoAproximadamenteEn = DateTimeOffset.UtcNow.AddHours(-1), ubicacion = PorTexto() } },
    };

    [Theory]
    [MemberData(nameof(SinCampoObligatorio))]
    public async Task CA02_SinUnCampoObligatorio_NoSeCreaElCaso(string campo, object cuerpo)
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync(Ruta, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains($"\"{campo}\"", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task CA02_DescripcionEnBlanco_NoSeCreaElCaso(string descripcion)
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync(Ruta, Solicitud(descripcion: descripcion));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task CA01_DP07_UnTipoFueraDelCatalogo_NoSeAcepta()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync(Ruta, Solicitud(categoria: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("\"categoriaId\"", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task DP08_LaDescripcion_AdmiteHasta2000Caracteres()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var limite = await app.PostAsJsonAsync(Ruta, Solicitud(descripcion: new string('a', DetalleReporte.MaximoDescripcion)));
        var excedida = await app.PostAsJsonAsync(Ruta, Solicitud(descripcion: new string('a', DetalleReporte.MaximoDescripcion + 1)));

        Assert.Equal(HttpStatusCode.Created, limite.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, excedida.StatusCode);
        Assert.Single(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task DP08_UnaFechaFutura_NoSeAcepta()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var manana = await app.PostAsJsonAsync(Ruta, Solicitud(ocurrido: DateTimeOffset.UtcNow.AddDays(1)));
        var fueraDelMargen = await app.PostAsJsonAsync(Ruta, Solicitud(ocurrido: DateTimeOffset.UtcNow + RegistrarReporte.MargenReloj + TimeSpan.FromMinutes(1)));

        Assert.Equal(HttpStatusCode.BadRequest, manana.StatusCode);
        Assert.Contains("no pueden ser futuras", await manana.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, fueraDelMargen.StatusCode);
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task DP08_ElMomentoActual_SeAceptaAunqueElTelefonoVayaAlgoAdelantado()
    {
        var (app, _) = await CiudadanoAsync();

        var ahora = await app.PostAsJsonAsync(Ruta, Solicitud(ocurrido: DateTimeOffset.UtcNow));
        var adelantado = await app.PostAsJsonAsync(Ruta, Solicitud(ocurrido: DateTimeOffset.UtcNow.AddMinutes(2)));

        Assert.Equal(HttpStatusCode.Created, ahora.StatusCode);
        Assert.Equal(HttpStatusCode.Created, adelantado.StatusCode);
    }

    [Fact]
    public async Task CA03_UbicacionPorReferenciaTextual_SeRegistraComoUbicacionDelHechoSinCoordenadas()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync(Ruta, Solicitud(PorTexto("  Pasaje Los Olivos, cerca del colegio  ")));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        var detalle = (await DetalleAsync(caso.Id))!;
        Assert.Equal(FuentesUbicacionHecho.Texto, detalle.FuenteUbicacion);
        Assert.Equal("Pasaje Los Olivos, cerca del colegio", detalle.ReferenciaHecho);
        Assert.Null(detalle.LatitudHecho);
        Assert.Null(detalle.LongitudHecho);
        // DP10: la referencia no genera un punto; y el caso no tiene «ubicación actual» del ciudadano.
        Assert.Null(caso.Latitud);
        Assert.Null(caso.UbicacionObtenidaEn);
    }

    [Fact]
    public async Task CA03_UbicacionPorMapa_SeRegistraComoUbicacionDelHecho_NoComoUbicacionActual()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        await app.PostAsJsonAsync(Ruta, Solicitud(EnMapa(-12.1, -77.02)));

        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        var detalle = (await DetalleAsync(caso.Id))!;
        Assert.Equal(FuentesUbicacionHecho.Mapa, detalle.FuenteUbicacion);
        Assert.Equal(-12.1, detalle.LatitudHecho);
        Assert.Equal(-77.02, detalle.LongitudHecho);
        // Las columnas de ubicación del caso son las del dispositivo (HU08) y quedan vacías.
        Assert.Null(caso.Latitud);
        Assert.Null(caso.Longitud);
        Assert.Null(caso.PrecisionMetros);
        Assert.False(caso.UbicacionDisponible);
    }

    [Fact]
    public async Task CA04_PosicionActualElegida_SeUsaComoUbicacionDelHecho()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        await app.PostAsJsonAsync(Ruta, Solicitud(new UbicacionHecho(FuentesUbicacionHecho.PosicionActual, -12.07, -77.04, 9.5, null)));

        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        var detalle = (await DetalleAsync(caso.Id))!;
        Assert.Equal(FuentesUbicacionHecho.PosicionActual, detalle.FuenteUbicacion);
        Assert.Equal(-12.07, detalle.LatitudHecho);
        Assert.Equal(9.5, detalle.PrecisionHechoMetros);
        Assert.Null(caso.Latitud);
    }

    [Fact]
    public async Task CA04_SinElegirLaPosicion_UnaReferenciaConCoordenadasSeRechaza()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        // Una app defectuosa que adjunta la posición del dispositivo a una ubicación por referencia.
        var respuesta = await app.PostAsJsonAsync(Ruta, Solicitud(new UbicacionHecho(FuentesUbicacionHecho.Texto, -12.07, -77.04, 9.5, "Por el parque")));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
    }

    [Theory]
    [InlineData(FuentesUbicacionHecho.Texto, null, null, null, null)]       // referencia vacía
    [InlineData(FuentesUbicacionHecho.Texto, null, null, null, "   ")]      // referencia en blanco
    [InlineData(FuentesUbicacionHecho.Mapa, null, null, null, "Plaza")]     // punto sin coordenadas
    [InlineData(FuentesUbicacionHecho.Mapa, -12.0, null, null, null)]       // punto incompleto
    [InlineData(FuentesUbicacionHecho.Mapa, 95.0, -77.0, null, null)]       // latitud fuera de rango
    [InlineData(FuentesUbicacionHecho.Mapa, -12.0, -190.0, null, null)]     // longitud fuera de rango
    [InlineData(FuentesUbicacionHecho.PosicionActual, -12.0, -77.0, 0.0, null)] // precisión no positiva
    [InlineData("Satelite", -12.0, -77.0, null, null)]                      // fuente desconocida
    [InlineData(null, -12.0, -77.0, null, null)]                            // sin fuente
    public async Task CA03_UbicacionDelHechoInvalida_NoSeCreaElCaso(string? fuente, double? lat, double? lon, double? precision, string? referencia)
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var respuesta = await app.PostAsJsonAsync(Ruta, Solicitud(new UbicacionHecho(fuente, lat, lon, precision, referencia)));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task DP10_LaReferencia_AdmiteHasta500Caracteres()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        var limite = await app.PostAsJsonAsync(Ruta, Solicitud(PorTexto(new string('r', DetalleReporte.MaximoReferencia))));
        var excedida = await app.PostAsJsonAsync(Ruta, Solicitud(PorTexto(new string('r', DetalleReporte.MaximoReferencia + 1))));

        Assert.Equal(HttpStatusCode.Created, limite.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, excedida.StatusCode);
        Assert.Single(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task CA06_UnReporte_NoGeneraNingunSos_NiAvisaALaAtencionUrgente()
    {
        await using var central = await fabrica.OperadorEnCentralAsync();
        var (app, ciudadano) = await CiudadanoAsync();

        var reporte = (await (await app.PostAsJsonAsync(Ruta, Solicitud())).Content.ReadFromJsonAsync<RespuestaReporte>())!;
        // Un SOS posterior sí avisa: cuando llega su aviso, el del reporte ya habría llegado si existiera.
        var sos = (await (await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(Guid.NewGuid(), DateTimeOffset.UtcNow, null)))
            .Content.ReadFromJsonAsync<RespuestaSos>())!;

        Assert.True(await CentralDePrueba.EsperarAsync(() => central.Avisos.Contains(sos.Id)));
        Assert.DoesNotContain(reporte.Id, central.Avisos);
        Assert.Single(await CasosDeAsync(ciudadano.Id), c => c.Origen == OrigenCaso.Sos);
        var pendientes = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarSosPendientes>().EjecutarAsync(CancellationToken.None));
        Assert.DoesNotContain(pendientes, p => p.Id == reporte.Id);
        Assert.Empty(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking()
            .Where(e => e.CasoId == reporte.Id && e.Tipo == TipoEventoCaso.SosEmitido).ToListAsync()));
    }

    [Fact]
    public async Task CA07_LaCategoriaElegida_QuedaGuardadaComoOriginal()
    {
        var (app, ciudadano) = await CiudadanoAsync();

        await app.PostAsJsonAsync(Ruta, Solicitud(categoria: Otros));

        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(Otros, (await DetalleAsync(caso.Id))!.CategoriaOriginalId);
        Assert.Equal(ciudadano.Id, caso.ClasificadoPor);
    }

    [Fact]
    public async Task CA08_ReiteracionTecnica_RegistraUnSoloReporteYDevuelveElMismo()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var solicitud = Solicitud();

        var primera = await app.PostAsJsonAsync(Ruta, solicitud);
        var reintento = await app.PostAsJsonAsync(Ruta, solicitud);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reintento.StatusCode);
        Assert.Equal((await primera.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id, (await reintento.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id);
        Assert.Single(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task CA08_ReintentosSimultaneos_RegistranUnSoloReporte()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var solicitud = Solicitud();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => app.PostAsJsonAsync(Ruta, solicitud)));

        Assert.All(respuestas, r => Assert.True(r.IsSuccessStatusCode, $"Respuesta {(int)r.StatusCode}"));
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Created));
        var ids = await Task.WhenAll(respuestas.Select(async r => (await r.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id));
        Assert.Single(ids.Distinct());
        var caso = Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.NotNull(await DetalleAsync(caso.Id));
        Assert.Equal(1, await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync(e => e.ActorId == ciudadano.Id)));
    }

    // Simula el reintento simultáneo que gana la carrera: justo antes de guardar, otra conexión registra el mismo reporte.
    private sealed class ReintentoQueGana(string conexion, Guid ciudadanoId, Guid idSolicitud) : SaveChangesInterceptor
    {
        public Guid? Ganador { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData datos,
            InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (Ganador is null)
            {
                await using var otro = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion).Options);
                var caso = Caso.RegistrarReporte(ciudadanoId, idSolicitud, Robo, DateTimeOffset.UtcNow);
                otro.Casos.Add(caso);
                otro.DetallesReporte.Add(DetalleReporte.Registrar(caso, Robo, "Hecho", DateTimeOffset.UtcNow, FuentesUbicacionHecho.Texto, null, null, null, "Plaza"));
                await otro.SaveChangesAsync(ct);
                Ganador = caso.Id;
            }
            return resultado;
        }
    }

    [Fact]
    public async Task CA08_SiOtroReintentoGanaLaCarrera_SeDevuelveSuReporteSinDuplicarNiFallar()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);
        var solicitud = Solicitud();
        var conexion = await fabrica.ConBaseDatosAsync(db => Task.FromResult(db.Database.GetConnectionString()!));
        var carrera = new ReintentoQueGana(conexion, ciudadano.Id, solicitud.IdSolicitud);
        await using var db = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion).AddInterceptors(carrera).Options);

        var resultado = await new RegistrarReporte(db, TimeProvider.System, fabrica.Services.GetRequiredService<Cad.Server.Compartido.INotificador>(), NullLogger<RegistrarReporte>.Instance)
            .EjecutarAsync(ciudadano.Id, solicitud, CancellationToken.None);

        var ya = Assert.IsType<ResultadoRegistroReporte.YaRegistrado>(resultado);
        Assert.Equal(carrera.Ganador, ya.Caso.Id);
        Assert.Single(await CasosDeAsync(ciudadano.Id));
        Assert.Empty(await fabrica.ConBaseDatosAsync(db => db.EventosCaso.AsNoTracking().Where(e => e.ActorId == ciudadano.Id).ToListAsync()));
    }

    [Fact]
    public async Task CA08_UnEnvioRechazado_NoDejaUnCasoParcial()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var eventosAntes = await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync());
        var detallesAntes = await fabrica.ConBaseDatosAsync(db => db.DetallesReporte.CountAsync());

        // Varios datos inválidos a la vez: se informan todos y no se guarda nada.
        var respuesta = await app.PostAsJsonAsync(Ruta, new SolicitudRegistroReporte(Guid.NewGuid(), Guid.NewGuid(), "", DateTimeOffset.UtcNow.AddDays(2), null));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("\"categoriaId\"", texto);
        Assert.Contains("\"descripcion\"", texto);
        Assert.Contains("\"ocurridoAproximadamenteEn\"", texto);
        Assert.Contains("\"ubicacion\"", texto);
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
        Assert.Equal(eventosAntes, await fabrica.ConBaseDatosAsync(db => db.EventosCaso.CountAsync()));
        Assert.Equal(detallesAntes, await fabrica.ConBaseDatosAsync(db => db.DetallesReporte.CountAsync()));
    }

    [Fact]
    public async Task CA08_UnIdentificadorYaUsadoEnUnSos_NoDevuelveEseSosComoReporte()
    {
        var (app, ciudadano) = await CiudadanoAsync();
        var id = Guid.NewGuid();
        await app.PostAsJsonAsync("/api/sos", new SolicitudEmisionSos(id, DateTimeOffset.UtcNow, null));

        var respuesta = await app.PostAsJsonAsync(Ruta, Solicitud() with { IdSolicitud = id });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(OrigenCaso.Sos, Assert.Single(await CasosDeAsync(ciudadano.Id)).Origen);
    }

    [Fact]
    public async Task CA03_CA04_LaBaseDeDatos_RechazaUnaReferenciaConCoordenadasAunqueFalleElCodigo()
    {
        var ciudadano = await fabrica.CrearAsync(Roles.Ciudadano);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fabrica.ConBaseDatosAsync(async db =>
        {
            var caso = Caso.RegistrarReporte(ciudadano.Id, Guid.NewGuid(), Robo, DateTimeOffset.UtcNow);
            db.Casos.Add(caso);
            db.DetallesReporte.Add(DetalleReporte.Registrar(caso, Robo, "Hecho", DateTimeOffset.UtcNow, FuentesUbicacionHecho.Texto, -12, -77, null, "Por el parque"));
            return await db.SaveChangesAsync();
        }));

        Assert.Contains("CK_DetallesReporte_Ubicacion", error.InnerException?.Message);
        Assert.Empty(await CasosDeAsync(ciudadano.Id));
    }

    [Fact]
    public async Task CA01_LaAppObtieneElCatalogoDeTipos()
    {
        var (app, _) = await CiudadanoAsync();

        var categorias = await app.GetFromJsonAsync<List<CategoriaCatalogo>>($"{Ruta}/categorias");

        // Catálogo ampliado por el PO el 22/10/2026, en orden de presentación; «Otros» siempre al final.
        Assert.Equal([
            "Robo o hurto", "Violencia familiar", "Acoso en la vía pública", "Extorsión o amenazas", "Consumo o venta de drogas",
            "Consumo de alcohol en la vía pública", "Peleas o riñas", "Persona o vehículo sospechoso", "Daños o vandalismo",
            "Accidente de tránsito", "Ruidos molestos o escándalo", "Persona en riesgo", "Otros",
        ], categorias!.Select(c => c.Nombre));
        // Cada categoría trae ejemplos para orientar al ciudadano y la clave de su icono.
        Assert.All(categorias, c => Assert.False(string.IsNullOrWhiteSpace(c.Ayuda) || string.IsNullOrWhiteSpace(c.Icono)));
        Assert.Equal(categorias.Count, categorias.Select(c => c.Icono).Distinct().Count());
    }

    [Fact]
    public async Task DP07_LasCategoriasOriginalesConservanSuIdentificador()
    {
        var (app, _) = await CiudadanoAsync();

        var categorias = await app.GetFromJsonAsync<List<CategoriaCatalogo>>($"{Ruta}/categorias");

        // Los casos anteriores al catálogo ampliado siguen apuntando a la misma categoría, solo cambia su nombre.
        Assert.Equal("Violencia familiar", categorias!.Single(c => c.Id == CategoriaIncidencia.ViolenciaFamiliar.Id).Nombre);
        Assert.Equal("Robo o hurto", categorias.Single(c => c.Id == CategoriaIncidencia.RoboOHurto.Id).Nombre);
        Assert.Equal("Consumo o venta de drogas", categorias.Single(c => c.Id == CategoriaIncidencia.Drogas.Id).Nombre);
        Assert.Equal("Otros", categorias.Single(c => c.Id == CategoriaIncidencia.Otros.Id).Nombre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.Operador)]
    [InlineData(Roles.Sereno)]
    [InlineData(Roles.Administrador)]
    public async Task HU03_SolicitudDirectaDeOtroPerfil_NoRegistraReportes(string? rol)
    {
        HttpClient cliente;
        if (rol is null)
            cliente = fabrica.CreateClient();
        else if (rol == Roles.Sereno)
            cliente = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(rol));
        else
        {
            var cuenta = rol == Roles.Administrador
                ? new CuentaDePrueba(Guid.Empty, fabrica.AdminCorreo, fabrica.AdminClave, rol)
                : await fabrica.CrearAsync(rol);
            cliente = fabrica.CrearNavegador();
            await cliente.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        }
        var casosAntes = await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync());

        var respuesta = await cliente.PostAsJsonAsync(Ruta, Solicitud());

        Assert.True(respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Se esperaba 401/403 y se obtuvo {(int)respuesta.StatusCode}");
        Assert.Equal(casosAntes, await fabrica.ConBaseDatosAsync(db => db.Casos.CountAsync()));
    }

    [Fact]
    public async Task RNF07_NiLasCoordenadasNiLaDescripcionNiLaReferencia_AparecenEnLosRegistros()
    {
        var (app, _) = await CiudadanoAsync();
        var ubicacion = EnMapa(-12.0876543, -77.0234567, "Calle Ficticia 742");

        await app.PostAsJsonAsync(Ruta, Solicitud(ubicacion, descripcion: "Descripción ficticia muy particular"));
        await app.PostAsJsonAsync(Ruta, Solicitud(ubicacion with { Latitud = 200 }, descripcion: "Descripción ficticia muy particular"));

        var datos = new[] { ubicacion.Latitud!.Value.ToString(CultureInfo.InvariantCulture), ubicacion.Longitud!.Value.ToString(CultureInfo.InvariantCulture),
            "Calle Ficticia 742", "Descripción ficticia muy particular" };
        foreach (var dato in datos)
            Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(dato));
    }
}
