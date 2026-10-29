using System.Net;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;

namespace Cad.Server.Tests.Analitica;

// «¿Cuánto de lo que entra es falso?»: la página dice qué cuenta como falso, muestra cada categoría con sus falsos «x de y» y reparte
// cómo terminan los reportes y los SOS en filas con su nombre y su cifra. Fechas propias de 2019. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class PaginaFalsosTests(FabricaCad fabrica)
{
    private static DateTimeOffset Local(int dia) => new DateTimeOffset(2019, 3, dia, 9, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    [Fact]
    public async Task ConReportesDecididos_ExplicaLaBarraYRepartePorFilas()
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var robo = CategoriaIncidencia.Iniciales[1].Id;
        await fabrica.ConBaseDatosAsync(async db =>
        {
            for (var i = 0; i < 4; i++)
            {
                var en = Local(2 + i);
                var caso = Caso.RegistrarReporte(ciudadano, Guid.NewGuid(), robo, en);
                db.Casos.Add(caso);
                db.DetallesReporte.Add(DetalleReporte.Registrar(caso, robo, "Descripción ficticia", en, "Texto", null, null, null, "Referencia", null, null, null, CanalesEntrada.App));
                db.DecisionesReporte.Add(i == 0
                    ? DecisionReporte.Registrar(caso, TipoDecisionReporte.Descarte, "Motivo ficticio", null, null, operador, en.AddHours(1), null, MotivosDescarte.FalsoOMalintencionado)
                    : DecisionReporte.Registrar(caso, TipoDecisionReporte.Admision, "Motivo ficticio", null, null, operador, en.AddHours(1), null, null));
            }
            return await db.SaveChangesAsync();
        });
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/analitica/falsos?periodo=rango&desde=2019-03-01&hasta=2019-03-28"));

        Assert.Contains("1 de cada 4 reportes decididos era falso", html);
        Assert.Contains("cuando la central lo descartó como «falso o malintencionado»", html);
        Assert.Contains("data-categoria=\"Robo o hurto\"", html);
        Assert.Contains("1 de 4", html); // los falsos de la categoría, con su base
        Assert.Contains("Todas las categorías: 25 %", html);
        Assert.Contains("La central los descartó", html);
        Assert.Contains("fila100__nombre\">Falsos<", html);
        Assert.Contains("data-fotos=\"Sin fotos\"", html);
    }
}
