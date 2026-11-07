using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Adaptadores;
using Cad.Server.Modulos.Asistencia.Aplicacion;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Modulos.Casos.Dominio;

namespace Cad.Server.Tests.Asistencia;

// Datos de prueba comunes: el catálogo real y una entrada mínima. Datos ficticios.
internal static class DatosAsistencia
{
    public static readonly IReadOnlyList<CategoriaParaAnalisis> Catalogo =
        CategoriaIncidencia.Iniciales.Select(c => new CategoriaParaAnalisis(c.Id, c.Nombre, c.Ayuda)).ToList();

    public static Guid Robo => CategoriaIncidencia.RoboOHurto.Id;
    public static Guid Otros => CategoriaIncidencia.Otros.Id;

    public static EntradaAnalisis Entrada(string descripcion, Guid? categoria = null, bool conPunto = true, string? referencia = null,
        IReadOnlyList<CandidatoDuplicado>? candidatos = null) =>
        new(Guid.NewGuid(), descripcion, categoria ?? Otros, conPunto ? "Mapa" : "Texto", conPunto, referencia,
            DateTimeOffset.UtcNow.AddMinutes(-20), DateTimeOffset.UtcNow, Catalogo, [], candidatos ?? []);

    public static AnalisisReporte Analisis(Guid? categoria = null, DecisionSugerida decision = DecisionSugerida.Admitir,
        NivelConfianza confianza = NivelConfianza.Alta, string motivo = "Hecho de seguridad ciudadana con ubicación.", bool urgencia = false,
        Guid? duplicado = null, string? destino = null, string? motivoDescarte = null, string? prioridad = PrioridadesCaso.Media) =>
        new(categoria ?? Robo, null, confianza, "Describe un robo al paso.", decision, motivo, destino, null, motivoDescarte, prioridad,
            urgencia, urgencia ? "Menciona un arma." : null, duplicado, []);
}

// Plan de asistencia con IA, 10 — Anonimizador: lo que sale del servidor no lleva teléfonos, DNI ni correos.
public class AnonimizadorTests
{
    [Theory]
    [InlineData("llámame al 987654321 por favor", "llámame al [dato] por favor")]
    [InlineData("mi número es 987 654 321", "mi número es [dato]")]
    [InlineData("escribe al +51 987-654-321", "escribe al [dato]")]
    [InlineData("su DNI es 45678912", "su DNI es [dato]")]
    [InlineData("el fijo 054 123456 no contesta", "el fijo [dato] no contesta")]
    [InlineData("mi correo es vecina.ficticia@correo.test", "mi correo es [dato]")]
    public void QuitaTelefonosDniYCorreos(string texto, string esperado) => Assert.Equal(esperado, Anonimizador.Limpiar(texto));

    [Theory]
    [InlineData("En la cuadra 3 de Av. Ejército 512, a las 15:30")]
    [InlineData("Eran 2 en moto y se fueron hacia el parque")]
    [InlineData("mi vecino del 3 escuchó gritos")] // sin un nombre propio después
    [InlineData("Soy de Arequipa y pasaba por ahí")]
    public void ConservaDireccionesHorasYNumerosCortos(string texto) => Assert.Equal(texto, Anonimizador.Limpiar(texto));

    // ADR-10, «Anonimización: ley y estándares»: las placas identifican al propietario; el asistente no las necesita.
    [Theory]
    [InlineData("Eran 2 en moto, placa ABC-123", "Eran 2 en moto, placa [placa]")]
    [InlineData("una camioneta a1b234 blanca", "una camioneta [placa] blanca")]
    public void QuitaPlacas(string texto, string esperado) => Assert.Equal(esperado, Anonimizador.Limpiar(texto));

    [Theory]
    [InlineData("Me llamo Rosa Ficticia y vi el robo", "Me llamo [nombre] y vi el robo")]
    [InlineData("el señor Juan Pérez Quispe gritaba", "el señor [nombre] gritaba")]
    [InlineData("mi vecina Marta salió a ayudar", "mi vecina [nombre] salió a ayudar")]
    [InlineData("la Sra. Lucía cerró la tienda", "la Sra. [nombre] cerró la tienda")]
    public void QuitaNombresPresentadosComoTales(string texto, string esperado) => Assert.Equal(esperado, Anonimizador.Limpiar(texto));

    [Fact]
    public void QuitaLosNombresConocidosDeQuienReporta_AunqueNoSePresenten()
    {
        var limpio = Anonimizador.Limpiar("rosa y su prima vieron a dos hombres frente a lo de Quispe; avisen a Marta",
            ["Rosa", "Ficticia Quispe", "Marta Ficticia", null]);

        Assert.Equal("[nombre] y su prima vieron a dos hombres frente a lo de [nombre]; avisen a [nombre]", limpio);
    }

    [Fact]
    public void LasPalabrasCortasDeUnNombre_NoSeQuitan() =>
        Assert.Equal("la casa de la esquina", Anonimizador.Limpiar("la casa de la esquina", ["María de la Cruz"]));
}

// HU44-CA05 y HU45-CA04/CA08 (plan, 5.5): la respuesta se valida antes de guardarla.
public class ValidarAnalisisTests
{
    [Fact]
    public void CategoriaFueraDelCatalogo_NoHaySugerencia() =>
        Assert.Null(ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(categoria: Guid.NewGuid()), DatosAsistencia.Entrada("texto")));

    [Fact]
    public void AlternativaFueraDelCatalogo_NoHaySugerencia() =>
        Assert.Null(ValidarAnalisis.Aplicar(DatosAsistencia.Analisis() with { CategoriaAlternativaId = Guid.NewGuid() }, DatosAsistencia.Entrada("texto")));

    [Fact]
    public void DecisionFueraDelEnum_NoHaySugerencia() =>
        Assert.Null(ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(decision: (DecisionSugerida)9), DatosAsistencia.Entrada("texto")));

    [Fact]
    public void DuplicadoQueNoEsCandidato_NoHaySugerencia()
    {
        var candidato = new CandidatoDuplicado(Guid.NewGuid(), "otro", "Robo o hurto", 40, 10);
        var entrada = DatosAsistencia.Entrada("texto", candidatos: [candidato]);

        Assert.Null(ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(duplicado: Guid.NewGuid()), entrada));
        var valido = ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(duplicado: candidato.CasoId), entrada);
        Assert.Equal(candidato.CasoId, valido!.DuplicadoDeId);
        Assert.Contains(SenalTriaje.Duplicado, valido.Senales);
    }

    [Fact]
    public void SinMotivo_SoloSeAceptaRevisarConCuidado()
    {
        Assert.Null(ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(motivo: "  "), DatosAsistencia.Entrada("texto")));
        Assert.NotNull(ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(decision: DecisionSugerida.RevisarConCuidado, motivo: ""),
            DatosAsistencia.Entrada("texto")));
    }

    [Fact]
    public void HU45_CA04_DescartarConPosibleUrgencia_SeReemplazaPorRevisarConCuidado()
    {
        var valido = ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(decision: DecisionSugerida.Descartar, urgencia: true,
            motivoDescarte: MotivosDescarte.FalsoOMalintencionado), DatosAsistencia.Entrada("texto"));

        Assert.Equal(DecisionSugerida.RevisarConCuidado, valido!.Decision);
        Assert.Null(valido.MotivoDescarteCodigo);
        Assert.Contains(SenalTriaje.PosibleUrgencia, valido.Senales);
    }

    [Fact]
    public void HU45_CA08_TextosLargosSeRecortanYLosCodigosAjenosSeQuitan()
    {
        var valido = ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(decision: DecisionSugerida.Derivar, motivo: new string('m', 3000),
            destino: "Inventado") with { Justificacion = new string('j', 900), AccionBorrador = new string('a', 2500) }, DatosAsistencia.Entrada("texto"));

        Assert.Equal(DecisionReporte.MaximoMotivo, valido!.MotivoBorrador.Length);
        Assert.Equal(300, valido.Justificacion.Length);
        Assert.Equal(DecisionReporte.MaximoAccion, valido.AccionBorrador!.Length);
        Assert.Null(valido.DestinoCodigo); // fuera del catálogo: queda vacío para elegirlo a mano
        Assert.Null(valido.Prioridad); // la prioridad solo acompaña a una admisión
    }

    [Fact]
    public void DerivarConDestinoDelCatalogo_LoConserva() =>
        Assert.Equal(DestinosDerivacion.Bomberos, ValidarAnalisis.Aplicar(DatosAsistencia.Analisis(decision: DecisionSugerida.Derivar,
            destino: DestinosDerivacion.Bomberos), DatosAsistencia.Entrada("texto"))!.DestinoCodigo);
}

// Plan, 5.1 — asistente por palabras clave (sin red): línea base y demostración del flujo sin clave.
public class AsistenteReglasTests
{
    private static AnalisisReporte Analizar(string texto, Guid? categoria = null, bool conPunto = true) =>
        AsistenteReglas.Analizar(DatosAsistencia.Entrada(texto, categoria, conPunto));

    [Fact]
    public void RoboConCategoriaEquivocada_SugiereRoboYAdmitir()
    {
        var a = Analizar("Me arrebataron el celular en la esquina del parque, eran dos ladrones en moto");

        Assert.Equal(DatosAsistencia.Robo, a.CategoriaSugeridaId);
        Assert.Equal(NivelConfianza.Alta, a.Confianza);
        Assert.Equal(DecisionSugerida.Admitir, a.Decision);
        Assert.Contains(SenalTriaje.CategoriaCiudadanoDudosa, a.Senales);
        Assert.Equal(PrioridadesCaso.Media, a.Prioridad);
    }

    [Fact]
    public void LeQuitaronElTelefono_EsRobo() =>
        Assert.Equal(DatosAsistencia.Robo, Analizar("Dos tipos en moto le quitaron el teléfono a un chico frente al parque").CategoriaSugeridaId);

    [Fact]
    public void TextoSinSentido_SugiereDescartar()
    {
        // Con una referencia del lugar: la referencia no cuenta como descripción del hecho.
        var a = AsistenteReglas.Analizar(DatosAsistencia.Entrada("asdf prueba", referencia: "Urb. Ficticia, frente al parque"));

        Assert.Equal(DecisionSugerida.Descartar, a.Decision);
        Assert.Equal(MotivosDescarte.FalsoOMalintencionado, a.MotivoDescarteCodigo);
        Assert.Contains(SenalTriaje.Spam, a.Senales);
    }

    [Fact]
    public void Incendio_SugiereDerivarABomberos()
    {
        var a = Analizar("Hay un incendio en el techo de una casa y sale mucho humo");

        Assert.Equal(DecisionSugerida.Derivar, a.Decision);
        Assert.Equal(DestinosDerivacion.Bomberos, a.DestinoCodigo);
        Assert.Contains(SenalTriaje.FueraDeAlcance, a.Senales);
    }

    [Fact]
    public void ConArma_MarcaPosibleUrgenciaSinConfianzaAltaNiDescarte()
    {
        var a = Analizar("Un sujeto con un cuchillo está asaltando a una señora en el paradero", DatosAsistencia.Robo);

        Assert.True(a.PosibleUrgencia);
        Assert.NotEqual(NivelConfianza.Alta, a.Confianza);
        Assert.NotEqual(DecisionSugerida.Descartar, a.Decision);
        Assert.Equal(PrioridadesCaso.Alta, a.Prioridad);
    }

    [Fact]
    public void SinPalabrasReconocibles_RevisarConCuidado()
    {
        var a = Analizar("Pasó algo raro por acá hace un rato, no sé bien qué");

        Assert.Equal(DecisionSugerida.RevisarConCuidado, a.Decision);
        Assert.Equal(NivelConfianza.Baja, a.Confianza);
    }

    [Fact]
    public void LasPalabrasSonRaices_NoSubcadenas() =>
        // «toco» (acoso) no debe encontrarse dentro de «protocolo».
        Assert.NotEqual(CategoriaIncidencia.Iniciales[4].Id, Analizar("Seguimos el protocolo de la junta vecinal").CategoriaSugeridaId);
}
