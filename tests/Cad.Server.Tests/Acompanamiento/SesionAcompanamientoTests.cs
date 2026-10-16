using Cad.Server.Modulos.Acompanamiento.Dominio;

namespace Cad.Server.Tests.Acompanamiento;

// Regla de cierre compartida de HU36 y HU37 (S4-T06, S4-T07), sin base de datos.
public class SesionAcompanamientoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HU34_CA01_LaSesionVenceTreintaMinutosDespuesDelInicio()
    {
        var sesion = SesionAcompanamiento.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Inicio);

        Assert.Equal((EstadoSesion.Activa, Inicio, Inicio.AddMinutes(30)), (sesion.Estado, sesion.IniciadaEn, sesion.VenceEn));
        Assert.True(sesion.AdmitePosiciones(Inicio.AddMinutes(29).AddSeconds(59)));
    }

    [Fact]
    public void HU37_CA04_DesdeElVencimiento_NoAdmitePosicionesAunqueSigaSinCerrar()
    {
        var sesion = SesionAcompanamiento.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Inicio);

        Assert.False(sesion.AdmitePosiciones(Inicio.AddMinutes(30)));
        Assert.Equal(EstadoSesion.Activa, sesion.Estado);
    }

    [Fact]
    public void HU36_CA02_FinalizarAntesDeVencer_QuedaFinalizadaConLaHoraDelCierre()
    {
        var sesion = SesionAcompanamiento.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Inicio);

        Assert.True(sesion.Finalizar(Inicio.AddMinutes(10)));

        Assert.Equal((EstadoSesion.Finalizada, Inicio.AddMinutes(10)), (sesion.Estado, sesion.CerradaEn));
        Assert.False(sesion.AdmitePosiciones(Inicio.AddMinutes(11)));
        Assert.False(sesion.Finalizar(Inicio.AddMinutes(12))); // HU36-CA07: un reenvío no cambia nada
        Assert.Equal(Inicio.AddMinutes(10), sesion.CerradaEn);
    }

    [Fact]
    public void HU37_UnaFinalizacionQueLlegaDespuesDelVencimiento_CierraPorVencimiento()
    {
        var sesion = SesionAcompanamiento.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Inicio);

        Assert.True(sesion.Finalizar(Inicio.AddMinutes(45)));

        Assert.Equal((EstadoSesion.Vencida, Inicio.AddMinutes(30)), (sesion.Estado, sesion.CerradaEn));
    }

    [Fact]
    public void HU37_CA01_VencerSoloDesdeLaHoraDeVencimiento()
    {
        var sesion = SesionAcompanamiento.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Inicio);

        Assert.False(sesion.Vencer(Inicio.AddMinutes(29)));
        Assert.True(sesion.Vencer(Inicio.AddMinutes(30)));
        Assert.Equal((EstadoSesion.Vencida, Inicio.AddMinutes(30)), (sesion.Estado, sesion.CerradaEn));
        Assert.False(sesion.Vencer(Inicio.AddMinutes(31)));
    }

    [Theory]
    [InlineData(-90.1, 0, 5, 0)]
    [InlineData(0, 180.1, 5, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 5, 6)]
    public void HU35_PosicionInvalida_NoSeCrea(double latitud, double longitud, double precision, int minutosFuturo)
    {
        Assert.Null(PosicionAcompanamiento.Crear(Guid.NewGuid(), latitud, longitud, precision, Inicio.AddMinutes(minutosFuturo), Inicio));
    }

    [Fact]
    public void HU35_CA03_PosicionAntigua_ConservaSuHora()
    {
        var posicion = PosicionAcompanamiento.Crear(Guid.NewGuid(), -12, -77, 5, Inicio.AddMinutes(-10), Inicio);

        Assert.Equal((Inicio.AddMinutes(-10), Inicio), (posicion!.ObtenidaEn, posicion.RecibidaEn));
    }
}
