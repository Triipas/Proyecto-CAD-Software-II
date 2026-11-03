using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cad.Contratos.Reportes;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Tests.Casos;

// DP08 ampliada (26/10/2026) — fotos del reporte (HU10): hasta tres JPEG de hasta 1 MB, sin metadatos, en la misma
// solicitud y con la misma idempotencia; solo el operador las ve en el detalle del reporte. Datos e imágenes ficticios.
[Collection(ColeccionServidor.Nombre)]
public class FotosReporteTests(FabricaCad fabrica)
{
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private const string Gps = "GPS-12.0631-77.0365";

    // JPEG mínimo bien formado (no se decodifica en el servidor): SOI, APP0 JFIF, metadatos opcionales (APP1 con EXIF y
    // una «ubicación», APP13 y un comentario), una tabla, el inicio de los datos comprimidos con su contenido y EOI.
    internal static byte[] Jpeg(bool conMetadatos = true, int relleno = 32, byte sello = 0x11)
    {
        var salida = new List<byte> { 0xFF, 0xD8 };
        void Segmento(byte marcador, byte[] carga)
        {
            salida.AddRange([0xFF, marcador, (byte)((carga.Length + 2) >> 8), (byte)((carga.Length + 2) & 0xFF)]);
            salida.AddRange(carga);
        }
        Segmento(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        if (conMetadatos)
        {
            Segmento(0xE1, [.. "Exif\0\0"u8, .. Encoding.ASCII.GetBytes(Gps)]);
            Segmento(0xED, [.. "Photoshop 3.0\0"u8, .. Encoding.ASCII.GetBytes("Autor ficticio")]);
            Segmento(0xFE, Encoding.ASCII.GetBytes("Comentario con " + Gps));
        }
        Segmento(0xDB, [0, .. Enumerable.Repeat((byte)1, 64)]);
        Segmento(0xDA, [1, 1, 0, 0, 63, 0]);
        salida.AddRange(Enumerable.Repeat(sello, relleno));
        salida.AddRange([0xFF, 0xD9]);
        return [.. salida];
    }

    private static SolicitudRegistroReporte Solicitud(IReadOnlyList<byte[]>? fotos, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Robo, "Robo de una bicicleta (ficticio)", DateTimeOffset.UtcNow.AddHours(-1),
            new UbicacionHecho(FuentesUbicacionHecho.Mapa, -12.0631, -77.0365, null, "Frente al mercado"), fotos);

    private async Task<HttpClient> CiudadanoAsync() => await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Ciudadano));

    private async Task<HttpClient> PanelAsync(string rol)
    {
        var cuenta = await fabrica.CrearAsync(rol);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);
        return navegador;
    }

    private static async Task<Guid> RegistrarAsync(HttpClient app, IReadOnlyList<byte[]>? fotos)
    {
        var respuesta = await app.PostAsJsonAsync("/api/reportes", Solicitud(fotos));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
    }

    private Task<List<FotoReporte>> FotosAsync(Guid casoId) =>
        fabrica.ConBaseDatosAsync(db => db.FotosReporte.AsNoTracking().Where(f => f.CasoId == casoId).OrderBy(f => f.Numero).ToListAsync());

    private static string RutaFoto(Guid id, int numero) => $"/panel/reportes/{id}?handler=Foto&numero={numero}";

    [Fact]
    public async Task ConTresFotos_SeGuardanEnOrdenYSinMetadatos()
    {
        var originales = new[] { Jpeg(sello: 0x21), Jpeg(sello: 0x22), Jpeg(conMetadatos: false, sello: 0x23) };

        var id = await RegistrarAsync(await CiudadanoAsync(), originales);

        var fotos = await FotosAsync(id);
        Assert.Equal([1, 2, 3], fotos.Select(f => f.Numero));
        Assert.Equal(Jpeg(conMetadatos: false, sello: 0x21), fotos[0].Contenido);
        Assert.Equal(Jpeg(conMetadatos: false, sello: 0x22), fotos[1].Contenido);
        Assert.Equal(originales[2], fotos[2].Contenido);
        Assert.All(fotos, f => Assert.DoesNotContain(Gps, Encoding.ASCII.GetString(f.Contenido)));
    }

    [Fact]
    public async Task SinFotos_ElReporteSeRegistraComoAntes()
    {
        var id = await RegistrarAsync(await CiudadanoAsync(), null);

        Assert.Empty(await FotosAsync(id));
    }

    public static TheoryData<string, byte[][]> FotosInvalidas => new()
    {
        { "fotos", [Jpeg(), Jpeg(), Jpeg(), Jpeg()] },
        { "fotos[1]", [Jpeg(), Jpeg(relleno: LimitesFotosReporte.MaximoBytes)] },
        { "fotos[0]", [Encoding.ASCII.GetBytes("\x89PNG no es un JPEG")] },
        { "fotos[0]", [[]] },
        { "fotos[0]", [[0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x40, 0x01]] }, // JPEG cortado
    };

    [Theory]
    [MemberData(nameof(FotosInvalidas))]
    public async Task FotosFueraDeLosLimites_NoSeCreaElReporte(string campo, byte[][] fotos)
    {
        var app = await CiudadanoAsync();
        var solicitud = Solicitud(fotos);

        var respuesta = await app.PostAsJsonAsync("/api/reportes", solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains($"\"{campo}\"", await respuesta.Content.ReadAsStringAsync());
        Assert.False(await fabrica.ConBaseDatosAsync(db => db.Casos.AnyAsync(c => c.IdSolicitud == solicitud.IdSolicitud)));
    }

    [Fact]
    public async Task ReintentoConElMismoIdSolicitud_NoDuplicaLasFotos()
    {
        var app = await CiudadanoAsync();
        var solicitud = Solicitud([Jpeg(), Jpeg(sello: 0x33)]);

        var primera = await app.PostAsJsonAsync("/api/reportes", solicitud);
        var segunda = await app.PostAsJsonAsync("/api/reportes", solicitud);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var id = (await primera.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id;
        Assert.Equal(id, (await segunda.Content.ReadFromJsonAsync<RespuestaReporte>())!.Id);
        Assert.Equal(2, (await FotosAsync(id)).Count);
    }

    [Fact]
    public async Task Operador_VeLasFotosEnElDetalleYLasDescargaSinCache()
    {
        var id = await RegistrarAsync(await CiudadanoAsync(), [Jpeg(sello: 0x41), Jpeg(sello: 0x42)]);
        var operador = await PanelAsync(Roles.Operador);

        var html = await operador.GetStringAsync($"/panel/reportes/{id}");
        var foto = await operador.GetAsync(RutaFoto(id, 2));

        Assert.Contains("data-fotos-reporte=\"2\"", html);
        Assert.Contains("data-foto=\"1\"", html);
        Assert.Contains("data-foto=\"2\"", html);
        Assert.DoesNotContain("data-foto=\"3\"", html);
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal("image/jpeg", foto.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", foto.Headers.CacheControl!.ToString());
        Assert.Equal(Jpeg(conMetadatos: false, sello: 0x42), await foto.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await operador.GetAsync(RutaFoto(id, 3))).StatusCode);
    }

    [Fact]
    public async Task ReporteSinFotos_ElDetalleLoIndica()
    {
        var id = await RegistrarAsync(await CiudadanoAsync(), null);
        var operador = await PanelAsync(Roles.Operador);

        var html = await operador.GetStringAsync($"/panel/reportes/{id}");

        Assert.Contains("data-fotos-reporte=\"0\"", html);
        Assert.Contains("data-sin-fotos", html);
    }

    [Fact]
    public async Task OtrosPerfiles_NoObtienenLasFotos()
    {
        var ciudadano = await CiudadanoAsync();
        var id = await RegistrarAsync(ciudadano, [Jpeg()]);
        var sereno = await fabrica.CrearClienteAppAsync(await fabrica.CrearAsync(Roles.Sereno));
        var administrador = fabrica.CrearNavegador();
        await administrador.IngresarAlPanelAsync(fabrica.AdminCorreo, fabrica.AdminClave);
        var anonimo = fabrica.CrearNavegador();

        // El titular y el sereno usan la app (token): el panel no los acepta. El administrador va a «acceso denegado».
        foreach (var cliente in new[] { ciudadano, sereno, administrador, anonimo })
        {
            var respuesta = await cliente.GetAsync(RutaFoto(id, 1));
            Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.NotEqual("image/jpeg", respuesta.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public void ElDespachoDelSereno_NoIncluyeLasFotos()
    {
        // DP06: el despacho es una proyección cerrada; las fotos no forman parte de ella.
        var tipo = typeof(Cad.Contratos.Serenos.DespachoSereno);
        Assert.DoesNotContain(tipo.GetProperties(), p => p.Name.Contains("Foto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LosRegistros_SoloDicenCuantasFotos()
    {
        var id = await RegistrarAsync(await CiudadanoAsync(), [Jpeg(sello: 0x51)]);
        var base64 = Convert.ToBase64String(Jpeg(sello: 0x51));

        Assert.Contains(fabrica.Registros.Entradas, e => e.Contains(id.ToString()) && e.Contains("fotos: 1"));
        Assert.DoesNotContain(fabrica.Registros.Entradas, e => e.Contains(Gps) || e.Contains(base64[..40]));
    }
}
