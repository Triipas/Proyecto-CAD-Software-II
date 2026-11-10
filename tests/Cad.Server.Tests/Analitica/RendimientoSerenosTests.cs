using System.Net;
using Cad.Contratos.Casos;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Modulos.Serenos.Dominio;
using Cad.Server.Panel.Pages.Analitica;
using Cad.Server.Persistencia;
using Cad.Server.Tests.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Analitica;

// «Rendimiento de los serenos» (03/11/2026): llegada, calificación, inicios fuera de rango, casos quitados, turno y zonas de cada
// sereno, comparados con el equipo. Fechas propias de marzo de 2007 y 2008 (cada prueba en su año: la base se comparte). Datos ficticios.
[Collection(ColeccionServidor.Nombre)]
public class RendimientoSerenosTests(FabricaCad fabrica)
{
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("Lima", TimeSpan.FromHours(-5), "Lima", "Lima");
    private static readonly Guid Robo = CategoriaIncidencia.Iniciales[1].Id;
    private static PeriodoAnalitica Marzo(int anio) => new(Local(anio, 1, 0), Local(anio, 28, 0), Lima);
    private static string Ruta(int anio, Guid? sereno = null) =>
        $"/panel/analitica/serenos?periodo=rango&desde={anio}-03-01&hasta={anio}-03-27{(sereno is null ? "" : $"&sereno={sereno}")}";

    private static DateTimeOffset Local(int anio, int dia, int hora, int minuto = 0) =>
        new DateTimeOffset(anio, 3, dia, hora, minuto, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    // Dos zonas de la cuadrícula, lejos de cualquier otra prueba.
    private const double LatA = -12.30, LonA = -76.80, LatB = -12.32, LonB = -76.82;

    private sealed record Escenario(Guid Rapida, Guid Lento, Guid Retirado, Guid Suplente);

    // Rápida: 5 casos, sale al minuto, llega a los 5, cierra a los 20, notas de 5, inicia siempre en rango; 8 h de turno, 6 en la
    // zona A y 2 en la B. Lento: 5 casos, sale a los 3, llega a los 15, cierra a los 45, notas de 2, 3 de 5 inicios fuera de rango.
    // Retirado: la central le quita sus 2 casos (no responde) y se los da al suplente, que no llega a atenderlos.
    private async Task<Escenario> SembrarAsync(int anio)
    {
        var ciudadano = (await fabrica.CrearAsync(Roles.Ciudadano)).Id;
        var operador = (await fabrica.CrearAsync(Roles.Operador)).Id;
        var rapida = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var lento = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var retirado = (await fabrica.CrearAsync(Roles.Sereno)).Id;
        var suplente = (await fabrica.CrearAsync(Roles.Sereno)).Id;

        await fabrica.ConBaseDatosAsync(async db =>
        {
            foreach (var (id, nombres, apellidos) in new[] { (rapida, "Ana", "Rápida Ficticia"), (lento, "Luis", "Lento Ficticio"), (retirado, "Rita", "Retirada Ficticia"), (suplente, "Saúl", "Suplente Ficticio") })
                await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Nombres, nombres).SetProperty(u => u.Apellidos, apellidos));

            void Evento(Caso caso, TipoEventoCaso tipo, Guid actor, DateTimeOffset en) =>
                db.EventosCaso.Add(EventoCaso.Registrar(caso, tipo, actor, VisibilidadEvento.Interna, en));

            // Un caso atendido y cerrado por el sereno, con los minutos de cada etapa desde la asignación.
            void Atendido(Guid sereno, int dia, int salida, int llegada, int cierre, int nota, bool? dentro, double lat, double lon)
            {
                var asignada = Local(anio, dia, 10);
                var caso = Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(lat, lon, 8, asignada.AddMinutes(-2), asignada.AddMinutes(-2)), asignada.AddMinutes(-2));
                caso.Asignar();
                caso.ConfirmarDesplazamiento();
                caso.IniciarAtencion();
                caso.Cerrar();
                db.Casos.Add(caso);
                var asignacion = Asignacion.Registrar(caso, sereno, operador, Guid.NewGuid(), asignada);
                asignacion.Finalizar(asignada.AddMinutes(cierre));
                db.Asignaciones.Add(asignacion);
                Evento(caso, TipoEventoCaso.CasoAsignado, operador, asignada);
                Evento(caso, TipoEventoCaso.DesplazamientoConfirmado, sereno, asignada.AddMinutes(salida));
                Evento(caso, TipoEventoCaso.AtencionIniciada, sereno, asignada.AddMinutes(llegada));
                Evento(caso, TipoEventoCaso.CasoCerrado, sereno, asignada.AddMinutes(cierre));
                db.IniciosAtencion.Add(InicioAtencion.Registrar(caso, sereno, (lat, lon, 8), dentro == false ? 400 : 20, 150, dentro,
                    dentro == false ? "Otro" : null, dentro == false ? "Motivo ficticio" : null, asignada.AddMinutes(llegada)));
                db.CierresCaso.Add(CierreCaso.Registrar(caso, Robo, ResultadosAtencion.ResueltoEnElLugar, "Resultado ficticio", null, sereno, asignada.AddMinutes(cierre)));
                db.EvaluacionesCaso.Add(EvaluacionCaso.Registrar(caso, ciudadano, nota, null, asignada.AddHours(3),
                    [nota >= 4 ? MotivosEvaluacion.LlegaronRapido : MotivosEvaluacion.Tardaron]));
            }

            for (var i = 0; i < 5; i++)
            {
                Atendido(rapida, 2 + i, 1, 5, 20, 5, true, LatA, LonA);
                Atendido(lento, 2 + i, 3, 15, 45, 2, i < 3 ? false : true, LatB, LonB);
            }

            for (var i = 0; i < 2; i++)
            {
                var asignada = Local(anio, 10 + i, 10);
                var caso = Caso.EmitirSos(ciudadano, Guid.NewGuid(), Posicion.ComoActual(LatA, LonA, 8, asignada, asignada), asignada);
                caso.Asignar();
                db.Casos.Add(caso);
                var primera = Asignacion.Registrar(caso, retirado, operador, Guid.NewGuid(), asignada);
                db.Asignaciones.Add(primera);
                await db.SaveChangesAsync();
                primera.Cancelar(asignada.AddMinutes(8));
                caso.Reasignar();
                await db.SaveChangesAsync();
                // Un sereno tiene una sola asignación vigente: la del suplente termina antes de la siguiente (sin llegar a atender).
                var nueva = Asignacion.Registrar(caso, suplente, operador, Guid.NewGuid(), asignada.AddMinutes(8));
                nueva.Finalizar(asignada.AddHours(2));
                db.Asignaciones.Add(nueva);
                db.Reasignaciones.Add(Reasignacion.Registrar(caso, retirado, suplente, operador, MotivosReasignacion.SerenoNoResponde, "Motivo ficticio", asignada.AddMinutes(8)));
                await db.SaveChangesAsync();
            }

            // El turno de la rápida (8 h) y sus posiciones: una cada 10 min, 6 h en la zona A y 2 h en la B.
            var inicio = Local(anio, 15, 8);
            var estado = EstadoSereno.Nuevo(rapida, inicio.AddDays(-1));
            estado.Cambiar(true, inicio);
            db.CambiosEstadoSereno.Add(CambioEstadoSereno.Registrar(estado));
            estado.Cambiar(false, inicio.AddHours(8));
            db.CambiosEstadoSereno.Add(CambioEstadoSereno.Registrar(estado));
            for (var m = 0; m < 8 * 60; m += 10)
            {
                var (lat, lon) = m < 6 * 60 ? (LatA, LonA) : (LatB, LonB);
                db.PosicionesSereno.Add(PosicionSereno.Crear(rapida, lat, lon, 8, inicio.AddMinutes(m), inicio.AddMinutes(m))!);
            }
            return await db.SaveChangesAsync();
        });
        return new Escenario(rapida, lento, retirado, suplente);
    }

    [Fact]
    public async Task MideCadaSereno_PorSusAsignaciones_YLoComparaConElEquipo()
    {
        var e = await SembrarAsync(2008);

        var equipo = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarRendimientoSerenos>().EjecutarAsync(Marzo(2008), CancellationToken.None));

        var rapida = equipo.Serenos.Single(s => s.SerenoId == e.Rapida);
        Assert.Equal("Ana Rápida Ficticia", rapida.Nombre);
        Assert.Equal((5, 5, 5), (rapida.CasosAsignados, rapida.CasosAtendidos, rapida.CasosCerrados));
        Assert.Equal((1.0, 5.0, 15.0), (rapida.ReaccionP50, rapida.LlegadaP50, rapida.AtencionP50));
        Assert.Equal((5, 5.0), (rapida.Evaluaciones, rapida.Calificacion));
        Assert.Equal(0, rapida.PorcentajeFueraDeRango);
        Assert.Equal(8, rapida.HorasOperativas, 3);
        // Las posiciones: 6 h en la zona A (la principal) y 2 h en la B; sus casos fueron todos en la A.
        Assert.Equal(Zona.De(LatA, LonA), rapida.ZonaPrincipal!.Zona);
        Assert.Equal((6.0, 5), (Math.Round(rapida.ZonaPrincipal.Horas, 3), rapida.ZonaPrincipal.Casos));
        Assert.Equal(2, rapida.Zonas.Single(z => z.Zona == Zona.De(LatB, LonB)).Horas, 3);

        var lento = equipo.Serenos.Single(s => s.SerenoId == e.Lento);
        Assert.Equal((15.0, 2.0, 60.0), (lento.LlegadaP50, lento.Calificacion, lento.PorcentajeFueraDeRango));
        Assert.Equal(5, lento.Resultados.Single(r => r.Codigo == ResultadosAtencion.ResueltoEnElLugar).Veces);
        Assert.Equal(5, lento.Motivos.Single(m => m.Codigo == MotivosEvaluacion.Tardaron).Veces);

        // El caso quitado cuenta para quien lo perdió, y la espera del suplente no se le cobra a nadie: no llegó.
        var retirado = equipo.Serenos.Single(s => s.SerenoId == e.Retirado);
        Assert.Equal((2, 2, 2, 0), (retirado.CasosAsignados, retirado.Retirados, retirado.RetiradosPorNoResponder, retirado.CasosAtendidos));
        var suplente = equipo.Serenos.Single(s => s.SerenoId == e.Suplente);
        Assert.Equal((2, 0, 0), (suplente.CasosAsignados, suplente.Retirados, suplente.Llegadas.Count));

        // El equipo: las diez llegadas juntas (cinco de 5 min y cinco de 15) y las diez notas (cinco de 5 y cinco de 2).
        Assert.Equal((10.0, 3.5, 30.0), (equipo.LlegadaP50, equipo.Calificacion, equipo.PorcentajeFueraDeRango));

        var opciones = new OpcionesAnalitica();
        Assert.Empty(Rendimiento.Senales(rapida, equipo, opciones));
        Assert.Equal(["llegada", "calificacion", "rango"], Rendimiento.Senales(lento, equipo, opciones).Select(s => s.Clave));
        Assert.Equal(["retiros"], Rendimiento.Senales(retirado, equipo, opciones).Select(s => s.Clave));
        Assert.Empty(Rendimiento.Senales(suplente, equipo, opciones));
    }

    [Fact]
    public async Task UnPeriodoSinSerenos_NoInventaCifras()
    {
        var vacio = new PeriodoAnalitica(Local(1979, 1, 0), Local(1979, 28, 0), Lima);

        var equipo = await fabrica.ConServiciosAsync(s => s.GetRequiredService<ConsultarRendimientoSerenos>().EjecutarAsync(vacio, CancellationToken.None));

        Assert.Empty(equipo.Serenos);
        Assert.Null(equipo.LlegadaP50);
        Assert.Null(equipo.Calificacion);
        Assert.Null(equipo.PorcentajeOcupado);
    }

    [Fact]
    public async Task LaLista_NombraACadaSereno_PoneArribaALosQueConvieneMirar_YExplicaCómoSeMide()
    {
        var e = await SembrarAsync(2007);
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync(Ruta(2007)));

        Assert.Contains("<em>2 de 4 serenos</em> tienen alguna señal frente al equipo.", html);
        Assert.Contains("1 tiene la calificación por debajo del equipo", html);
        Assert.Contains("1 tiene casos reasignados", html);
        // El lento va primero (tres señales); la rápida, sin señales.
        Assert.True(html.IndexOf("Luis Lento Ficticio", StringComparison.Ordinal) < html.IndexOf("Ana Rápida Ficticia", StringComparison.Ordinal));
        Assert.Contains($"data-sereno=\"{e.Lento}\" data-senales=\"3\"", html);
        Assert.Contains($"data-sereno=\"{e.Rapida}\" data-senales=\"0\"", html);
        Assert.Contains("▲ Llega más lento que el equipo", html);
        // Las franjas del equipo, las fichas de los que tienen señales y los demás aparte.
        Assert.Equal(4, html.Split("class=\"franja-b\"").Length - 1);
        Assert.Contains("Para mirar primero", html);
        Assert.Equal(2, html.Split("class=\"ficha-b\"").Length - 1);
        Assert.Contains("Sin señales", html);
        Assert.Contains("Cómo se mide cada cifra", html);
        Assert.Contains("aria-current=\"page\" data-pregunta=\"serenos\"", html);
        Assert.Contains("▲ 2 serenos con señales", html);
    }

    [Fact]
    public async Task ElDetalle_DiceLaLlegadaFrenteAlEquipo_YLaColumnaDeOtrasPreguntasNoNombraASerenos()
    {
        var e = await SembrarAsync(2006);
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var detalle = WebUtility.HtmlDecode(await navegador.GetStringAsync(Ruta(2006, e.Lento)));

        Assert.Contains("Luis Lento Ficticio llega en una mediana de <em>15 min</em> (el equipo, 10 min) y su atención se califica con <em>2 de 5</em>.", detalle);
        Assert.Contains("data-senal=\"rango\"", detalle);
        Assert.Contains("Frente al equipo", detalle);
        Assert.Equal(5, detalle.Split("class=\"frente-b__fila\"").Length - 1);
        Assert.Contains("data-escala=\"frecuencia\"", detalle);
        Assert.Contains("Todos los serenos", detalle);
        // Con la lista calculada, otra pregunta muestra el dato de esta en su columna, sin ningún nombre.
        var otra = WebUtility.HtmlDecode(await navegador.GetStringAsync("/panel/analitica/reasigna?periodo=rango&desde=2006-03-01&hasta=2006-03-27"));
        Assert.Contains("▲ 2 serenos con señales", otra);
        Assert.DoesNotContain("Lento Ficticio", otra);
    }

    [Fact]
    public async Task UnSerenoSinTurnosNiCasosEnElPeriodo_MuestraElEstadoVacio()
    {
        var sereno = await fabrica.CrearAsync(Roles.Sereno);
        var cuenta = await fabrica.CrearAsync(Roles.Operador);
        var navegador = fabrica.CrearNavegador();
        await navegador.IngresarAlPanelAsync(cuenta.Correo, cuenta.Clave);

        var html = WebUtility.HtmlDecode(await navegador.GetStringAsync(
            $"/panel/analitica/serenos?periodo=rango&desde=1979-03-01&hasta=1979-03-27&sereno={sereno.Id}"));

        Assert.Contains("Este sereno no estuvo en turno ni tuvo casos en el periodo del", html);
        Assert.Contains("data-estado=\"vacio\"", html);
    }
}
