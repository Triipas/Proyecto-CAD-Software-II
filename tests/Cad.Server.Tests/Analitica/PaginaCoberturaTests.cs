using System.Net;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Analitica;

// «¿Dónde falta cobertura?»: el mapa con su leyenda (más o menos cubierta, sin colores solos), la hora de más presión escrita y la
// lista de zonas con el tiempo sin sereno cerca. Fechas propias de 2018. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class PaginaCoberturaTests(FabricaCad fabrica)
{
    private static DateTimeOffset Local(int dia, int hora) => new DateTimeOffset(2018, 3, dia, hora, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    [Fact]
    public async Task ConIncidenciasEnUnaZona_ExplicaElMapaLosTurnosYLaLista()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConBaseDatosAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
            {
                var en = Local(2 + i, 9 + i);
                db.Casos.Add(Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(-12.0525, -77.0425, 5, en, en), en));
            }
            return await db.SaveChangesAsync();
        });
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/analitica/cobertura?periodo=rango&desde=2018-03-01&hasta=2018-03-07"));

        Assert.Contains("data-escala=\"cobertura\"", html);
        Assert.Contains("Más cubierta", html);
        Assert.Contains("Menos de la mitad del tiempo cubierta", html);
        Assert.Contains("¿Los turnos acompañan a la demanda?", html);
        Assert.Contains("La hora de más presión es de", html);
        Assert.Contains("Cada gráfico tiene su propia escala", html);
        Assert.Contains("Tiempo sin sereno cerca", html);
    }
}
