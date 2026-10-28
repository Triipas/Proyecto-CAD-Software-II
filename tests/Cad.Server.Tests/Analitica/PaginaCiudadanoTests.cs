using System.Net;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Analitica;

// «¿Qué opina el ciudadano?»: la página explica la relación entre la nota y la espera, y reparte los canales de los reportes en
// filas con su nombre y su cifra. Fechas propias de 2015. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class PaginaCiudadanoTests(FabricaCad fabrica)
{
    private static DateTimeOffset Local(int dia) => new DateTimeOffset(2015, 3, dia, 9, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    [Fact]
    public async Task ConReportesPorCanal_LosRepartePorFilas()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var robo = CategoriaIncidencia.Iniciales[1].Id;
        await fabrica.ConBaseDatosAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
            {
                var en = Local(2 + i);
                var caso = Caso.RegistrarReporte(ciudadano, Guid.NewGuid(), robo, en);
                db.Casos.Add(caso);
                db.DetallesReporte.Add(DetalleReporte.Registrar(caso, robo, "Descripción ficticia", en, "Texto", null, null, null, "Referencia", null, null, null, CanalesEntrada.App));
            }
            return await db.SaveChangesAsync();
        });
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/analitica/ciudadano?periodo=rango&desde=2015-03-01&hasta=2015-03-28"));

        Assert.Contains("Por dónde llegan los reportes", html);
        Assert.Contains("fila100__nombre\">App CAD Móvil<", html);
        Assert.Contains("Reportes enviados en la primera hora del hecho", html);
    }
}
