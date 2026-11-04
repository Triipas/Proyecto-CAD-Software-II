using Cad.DatosDemo;
using Cad.Proyeccion;
using Cad.Server.Modulos.Consultas;
using Cad.Server.Modulos.Proyeccion;

namespace Cad.Server.Tests.Proyeccion;

// HU48 (S6-T18; plan, 8.3 a 8.6 y 9.3) — piezas de la proyección sin base de datos. Datos simulados.
public class ProyeccionUnidadTests
{
    private static readonly DateTimeOffset LunesDiezDeLaManana = new(2003, 6, 2, 10, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void Bloques_DeCuatroHorasEnHoraLocal_ConDiaYFranja()
    {
        var bloque = Bloques.De(LunesDiezDeLaManana);

        Assert.Equal(new DateTimeOffset(2003, 6, 2, 8, 0, 0, TimeSpan.FromHours(-5)), Bloques.Inicio(bloque));
        Assert.Equal((0, 2, 2), (Bloques.DiaSemana(bloque), Bloques.DelDia(bloque), Bloques.Franja(bloque)));
        Assert.Equal("lunes 08–12 h", Bloques.TextoFranja(bloque));
        Assert.Equal(bloque + Bloques.PorSemana, Bloques.De(LunesDiezDeLaManana.AddDays(7)));
    }

    [Fact]
    public void Celda_EsLaMismaCuadriculaQueElMapaDeConcentracion()
    {
        var celda = Celda.De(-16.4234, -71.5365);

        Assert.Equal(ConsultarConcentracion.TamanoCelda, Celda.Tamano);
        Assert.InRange(-16.4234, celda.Sur, celda.Norte);
        Assert.InRange(-71.5365, celda.Oeste, celda.Este);
        Assert.Equal(8, celda.Vecinas().Distinct().Count());
        Assert.Equal(celda, Celda.DeClave(celda.Clave));
    }

    [Fact]
    public void Metricas_AciertoYPai()
    {
        // 20 celdas: el 10 % son 2. Los casos caen en las dos que más se esperaban: acierto 100 %, PAI 10.
        var esperados = Enumerable.Range(0, 20).Select(i => i < 2 ? 5.0 : 0.1).ToList();
        var reales = Enumerable.Range(0, 20).Select(i => i < 2 ? 3 : 0).ToList();

        var r = Metricas.Evaluar(esperados, reales);

        Assert.Equal((1.0, 10.0, 6), (r.Acierto, r.Pai, r.Casos));
        Assert.Equal(0.5, Metricas.Evaluar(esperados, Enumerable.Range(0, 20).Select(i => i is 0 or 5 ? 1 : 0).ToList()).Acierto);
    }

    [Fact]
    public void Niveles_CincoPorCientoAltoYQuincePorCientoMedio()
    {
        var esperados = Enumerable.Range(1, 40).Select(i => (double)i).Append(0).ToArray();

        var niveles = ConsultarProyeccion.Niveles(esperados);

        Assert.Equal(2, niveles.Count(n => n == NivelFoco.Alto));   // 5 % de 40
        Assert.Equal(6, niveles.Count(n => n == NivelFoco.Medio));  // 20 % − 5 %
        Assert.Equal(NivelFoco.Alto, niveles[39]);
        Assert.Equal(NivelFoco.Bajo, niveles[40]);                  // sin casos esperados: no compite
    }

    [Fact]
    public void Ventanas_ProximasSeisHorasEstaNocheYManana()
    {
        var ahora = LunesDiezDeLaManana.ToUniversalTime();

        var (d6, h6, _) = ConsultarProyeccion.Ventana(VentanaProyeccion.ProximasSeisHoras, ahora);
        var (dn, hn, _) = ConsultarProyeccion.Ventana(VentanaProyeccion.EstaNoche, ahora);
        var (dm, hm, _) = ConsultarProyeccion.Ventana(VentanaProyeccion.Manana, ahora);
        var fracciones = ConsultarProyeccion.Fracciones(d6, h6);

        Assert.Equal(6, (h6 - d6).TotalHours);
        Assert.Equal((18, 0), (dn.ToOffset(Bloques.HusoHorario).Hour, hn.ToOffset(Bloques.HusoHorario).Hour));
        Assert.Equal(24, (hm - dm).TotalHours);
        // 10:00–16:00: la mitad del bloque de 8–12 (2 h) y el de 12–16 entero.
        Assert.Equal([0.5, 1.0], fracciones.Select(f => f.Fraccion));
    }

    // CA06 (plan, 8.4): la línea base tiene memoria corta. Una zona que empezó a tener casos hace poco sube por encima de una que tuvo
    // más casos hace meses, aunque un promedio sin memoria diga lo contrario.
    [Fact]
    public void CA06_LineaBase_UnFocoRecienteSubeMasRapidoQueConUnPromedioSinMemoria()
    {
        var objetivo = Bloques.De(LunesDiezDeLaManana);
        var antiguo = Celda.De(-16.4300, -71.5300);
        var reciente = Celda.De(-16.4150, -71.5150);
        var casos = new List<CasoHistorico>();
        for (var k = 20; k <= 26; k++)
            for (var n = 0; n < 3; n++)
                casos.Add(new(Bloques.Inicio(objetivo - (long)k * Bloques.PorSemana).AddMinutes(30 + n), antiguo.LatitudCentro, antiguo.LongitudCentro));
        for (var k = 1; k <= 3; k++)
            for (var n = 0; n < 2; n++)
                casos.Add(new(Bloques.Inicio(objetivo - (long)k * Bloques.PorSemana).AddMinutes(30 + n), reciente.LatitudCentro, reciente.LongitudCentro));
        var h = new Historial(casos);
        var linea = new LineaBase(h, new ParametrosLineaBase(Suavizado: 0));

        Assert.True(h.Casos(antiguo, h.PrimerBloque, objetivo) > h.Casos(reciente, h.PrimerBloque, objetivo)); // sin memoria gana el antiguo
        Assert.True(linea.Calcular(reciente, objetivo) > linea.Calcular(antiguo, objetivo));
    }

    [Fact]
    public void Rasgos_SoloMiranElPasado()
    {
        var objetivo = Bloques.De(LunesDiezDeLaManana);
        var celda = Celda.De(-16.4234, -71.5365);
        var futuro = new CasoHistorico(Bloques.Inicio(objetivo).AddHours(1), celda.LatitudCentro, celda.LongitudCentro);
        var pasado = new CasoHistorico(Bloques.Inicio(objetivo - Bloques.PorSemana).AddHours(1), celda.LatitudCentro, celda.LongitudCentro);
        var h = new Historial([pasado, futuro]);

        var f = ConstructorRasgos.Construir(h, new LineaBase(h), celda, objetivo);

        Assert.Equal((1f, 0f), (f.Semana1, f.Ultimas24h));
        Assert.Equal(1f, f.Celda8Semanas); // el caso del propio bloque no entra
    }

    // S6-T02: el generador es reproducible y los focos aparecen donde se plantaron (plan, 4.6, comprobación 1).
    [Fact]
    public void Generador_EsReproducibleYElFocoDelMercadoEsLaCeldaConMasCasos()
    {
        var fin = new DateTimeOffset(2003, 6, 2, 0, 0, 0, TimeSpan.Zero);
        var a = GeneradorMundo.Generar("A", 6, 7, fin);
        var b = GeneradorMundo.Generar("A", 6, 7, fin);

        Assert.Equal(a.Casos.Count, b.Casos.Count);
        Assert.Equal(a.Casos[100].Id, b.Casos[100].Id);
        Assert.InRange(a.Casos.Count / 42.0, 15, 45); // unos 30 por día
        var masCasos = a.Casos.GroupBy(c => Celda.De(c.Latitud, c.Longitud)).MaxBy(g => g.Count())!.Key;
        Assert.Equal(Celda.De(-16.4234, -71.5365), masCasos);
        Assert.All(a.Casos, c => Assert.InRange(c.Latitud, GeneradorMundo.Sur, GeneradorMundo.Norte));
        Assert.True(a.Intensidad.Count > 0);
    }

    // CA09: el parte no puede traer cifras, zonas ni serenos que no estén en su entrada.
    [Fact]
    public void CA09_ElParteConUnaCifraZonaOSerenoInventados_NoPasaLaValidacion()
    {
        var zonas = new List<ZonaProyectada>
        {
            new("1:1", "Zona K10", -16.42, -71.53, -16.415, -71.525, 3.2, NivelFoco.Alto, "Lunes 08–12 h: 9 casos.", "robo o hurto", 2100, Guid.NewGuid(), "Ana Ficticia", true),
        };
        var serenos = new List<(string, Guid, string)> { ("S1", zonas[0].SerenoCercanoId!.Value, "Ana Ficticia") };
        const string datos = """{"zonas":[{"id":"Z1","zona":"Zona K10","nivel":"Alto","casosEsperados":"3.2","explicacion":"Lunes 08–12 h: 9 casos.","serenoId":"S1","distanciaSereno":"2.1 km"}]}""";
        string Respuesta(string resumen, string zona = "Z1", string? sereno = "S1", string texto = "Reforzar Z1; S1 está a 2.1 km.") =>
            System.Text.Json.JsonSerializer.Serialize(new { resumen, acciones = new[] { new { zonaId = zona, texto, serenoId = sereno } } });

        var valido = RedactarParte.Validar(Respuesta("En Z1 se esperan 3.2 casos."), datos, serenos, zonas);

        Assert.NotNull(valido);
        Assert.Equal("En Zona K10 se esperan 3.2 casos.", valido.Resumen);
        Assert.Equal("Reforzar Zona K10; Ana Ficticia está a 2.1 km.", valido.Acciones[0].Texto);
        Assert.Null(RedactarParte.Validar(Respuesta("En Z1 se esperan 7 casos."), datos, serenos, zonas));       // cifra inventada
        Assert.Null(RedactarParte.Validar(Respuesta("En Z1 se esperan 3.2 casos.", zona: "Z4"), datos, serenos, zonas)); // zona inventada
        Assert.Null(RedactarParte.Validar(Respuesta("En Z1 se esperan 3.2 casos.", sereno: "S9"), datos, serenos, zonas)); // sereno inventado
        Assert.Null(RedactarParte.Validar("no es JSON", datos, serenos, zonas));
    }
}
