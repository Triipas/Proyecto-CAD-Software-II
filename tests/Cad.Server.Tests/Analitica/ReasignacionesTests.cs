using System.Net;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Analitica;

// «¿Se reasigna mucho?» (29/10/2026): cuántos casos asignados se reasignaron, por qué, cuánto los tuvo el primer sereno y a
// cuántos serenos se les quitó (sin nombres). Fechas propias de marzo de 2010, 2011 y 2012. Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class ReasignacionesTests(FabricaCad fabrica)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    // Cada prueba siembra en su propio año: la base se comparte entre las pruebas y un periodo ajeno no debe sumar.
    private static PeriodoAnalitica Marzo(int anio) => new(Local(anio, 1, 0), Local(anio, 28, 0), Lima);
    private static string Ruta(int anio) => $"/panel/analitica/reasigna?periodo=rango&desde={anio}-03-01&hasta={anio}-03-27";

    private static DateTimeOffset Local(int anio, int dia, int minuto) =>
        new DateTimeOffset(anio, 3, dia, 10, minuto, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    // Cuatro casos asignados: el 1 se reasigna una vez (a los 10 min, «no responde»); el 2 no se reasigna; el 3 se reasigna dos veces
    // (a los 20 min por «otro más cerca» y 10 min después por «no responde»); el 4 pierde su sereno porque se desactiva su cuenta.
    private async Task SembrarAsync(int anio)
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var serenos = new List<Guid>();
        for (var i = 0; i < 6; i++)
            serenos.Add((await fabrica.CrearAsync(Roles.Sereno)).Id);

        await fabrica.ConBaseDatosAsync(async db =>
        {
            Caso NuevoCaso(int dia)
            {
                var en = Local(anio, dia, 0);
                var caso = Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(-12.05, -77.04, 8, en, en), en);
                db.Casos.Add(caso);
                return caso;
            }
            Asignacion Asignar(Caso caso, Guid sereno, DateTimeOffset en)
            {
                var asignacion = Asignacion.Registrar(caso, sereno, operador, Guid.NewGuid(), en);
                db.Asignaciones.Add(asignacion);
                return asignacion;
            }
            // Como ReasignarCaso: primero deja de estar vigente la anterior y después se registra la nueva.
            async Task<Asignacion> Reasignar(Caso caso, Asignacion anterior, Guid nuevo, string motivo, DateTimeOffset en)
            {
                anterior.Cancelar(en);
                caso.Reasignar();
                await db.SaveChangesAsync();
                var asignacion = Asignar(caso, nuevo, en);
                db.Reasignaciones.Add(Reasignacion.Registrar(caso, anterior.SerenoId, nuevo, operador, motivo, "Motivo ficticio", en));
                await db.SaveChangesAsync();
                return asignacion;
            }

            var primero = NuevoCaso(2);
            primero.Asignar();
            var a1 = Asignar(primero, serenos[0], Local(anio, 2, 1));
            await db.SaveChangesAsync();
            await Reasignar(primero, a1, serenos[1], MotivosReasignacion.SerenoNoResponde, Local(anio, 2, 11));

            var segundo = NuevoCaso(3);
            segundo.Asignar();
            Asignar(segundo, serenos[2], Local(anio, 3, 1));
            await db.SaveChangesAsync();

            var tercero = NuevoCaso(4);
            tercero.Asignar();
            var a3 = Asignar(tercero, serenos[3], Local(anio, 4, 0));
            await db.SaveChangesAsync();
            var a3b = await Reasignar(tercero, a3, serenos[4], MotivosReasignacion.OtroMasCercano, Local(anio, 4, 20));
            await Reasignar(tercero, a3b, serenos[5], MotivosReasignacion.SerenoNoResponde, Local(anio, 4, 30));

            // El sereno del primer caso, ya sin él, atiende un cuarto caso y su cuenta se desactiva: no es una reasignación.
            var cuarto = NuevoCaso(5);
            cuarto.Asignar();
            var a4 = Asignar(cuarto, serenos[0], Local(anio, 5, 1));
            await db.SaveChangesAsync();
            a4.Cancelar(Local(anio, 5, 15));
            cuarto.LiberarPorCancelacion();
            return await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task CuentaLosCasosReasignados_ElMotivo_LaEsperaYLosSerenos_SinContarLaDesactivacion()
    {
        await SembrarAsync(2012);

        var r = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarReasignaciones>().EjecutarAsync(Marzo(2012), CancellationToken.None));

        Assert.Equal((4, 2, 3, 1), (r.CasosAsignados, r.CasosReasignados, r.Reasignaciones, r.CasosReasignadosVariasVeces));
        Assert.Equal(50, r.PorcentajeReasignados);
        Assert.Equal(2, r.Motivos.Single(m => m.Codigo == MotivosReasignacion.SerenoNoResponde).Veces);
        Assert.Equal(1, r.Motivos.Single(m => m.Codigo == MotivosReasignacion.OtroMasCercano).Veces);
        Assert.Equal(0, r.Motivos.Single(m => m.Codigo == MotivosReasignacion.FallaDelEquipo).Veces);
        Assert.DoesNotContain(r.Motivos, m => m.Codigo == ConsultarReasignaciones.SinMotivo);
        Assert.Equal(1, r.RetirosPorDesactivacion);
        Assert.Equal((6, 3, 1), (r.SerenosConAsignacion, r.SerenosConRetiros, r.MaximoRetirosDeUnSereno));
        // El primer sereno tuvo el caso 10, 20 y 10 min: la mediana son 10.
        Assert.Equal((3, 10.0), (r.Esperas, r.EsperaP50Minutos));
    }

    [Fact]
    public async Task UnPeriodoSinAsignaciones_NoInventaUnPorcentaje()
    {
        var vacio = new PeriodoAnalitica(Local(1980, 1, 0), Local(1980, 28, 0), Lima);

        var r = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarReasignaciones>().EjecutarAsync(vacio, CancellationToken.None));

        Assert.Null(r.PorcentajeReasignados);
        Assert.Equal((0, 0, 0), (r.CasosAsignados, r.Reasignaciones, r.Esperas));
        Assert.Null(r.EsperaP50Minutos);
    }

    [Fact]
    public async Task ElInformeDelPeriodo_LlevaLosCasosReasignados()
    {
        await SembrarAsync(2011);

        var informe = await fabrica.ConServiciosAsync(s => s.GetRequiredService<GenerarInformePeriodo>().EjecutarAsync(Marzo(2011), CancellationToken.None));

        Assert.Equal(50, informe.Indicadores.Single(i => i.Nombre == "Casos reasignados").Actual);
        Assert.Equal(3, informe.Indicadores.Single(i => i.Nombre == "Reasignaciones").Actual);
    }

    [Fact]
    public async Task LaPregunta_DiceElPorcentaje_ElMotivo_YNoNombraASerenosNiOperadores()
    {
        await SembrarAsync(2010);
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync(Ruta(2010)));

        Assert.Contains("Se reasignó el <em>50 %</em> de los casos asignados (2 de 4), por encima de la referencia de 10 %.", html);
        Assert.Contains("data-senal=\"reasignados\" data-bien=\"false\"", html);
        Assert.Contains("¿Por qué se reasigna?", html);
        Assert.Contains("El sereno no responde", html);
        Assert.Contains("Retirados por desactivar la cuenta", html);
        // La columna de preguntas la incluye, con su dato del periodo, y el nombre de ningún sereno aparece.
        Assert.Contains("aria-current=\"page\" data-pregunta=\"reasigna\"", html);
        Assert.Contains("▲ 50 % reasignados", html);
        Assert.DoesNotContain("Prueba Sereno", html);
    }

    [Fact]
    public async Task SinCasosAsignados_MuestraElEstadoVacio()
    {
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/analitica/reasigna?periodo=rango&desde=1980-03-01&hasta=1980-03-27"));

        Assert.Contains("No se asignó ningún caso en el periodo del", html);
        Assert.Contains("data-estado=\"vacio\"", html);
    }
}
