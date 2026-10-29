using System.Net;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Analitica;

// «¿Funciona el sistema?»: cuenta cada señal fuera de lo aceptable una sola vez, rotula lo que mide cada paso de la cadena y
// explica la leyenda de las señales. Fechas propias de 2014. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class PaginaSistemaTests(FabricaCad fabrica)
{
    private static DateTimeOffset Local(int dia) => new DateTimeOffset(2014, 3, dia, 10, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    [Fact]
    public async Task ConSosSinUbicacion_LoDiceUnaVezYExplicaLaCadena()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        await fabrica.ConBaseDatosAsync(async db =>
        {
            for (var i = 0; i < 5; i++)
            {
                var en = Local(2 + i);
                db.Casos.Add(Caso.EmitirSos(ciudadano, Guid.NewGuid(), i < 2 ? null : Posicion.ComoActual(-16.43, -71.53, 12, en, en), en));
            }
            return await db.SaveChangesAsync();
        });
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/analitica/sistema?periodo=rango&desde=2014-03-01&hasta=2014-03-28"));

        Assert.Contains("Hay que mirar <em>1 señal</em>: «SOS sin ubicación» está en 40 %", html);
        Assert.Contains("cadena-a__mide", html);
        Assert.Contains("el SOS se registra al llegar: no hay tiempo que medir", html);
        Assert.Contains("Franja aceptable (provisional", html);
    }
}
