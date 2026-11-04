namespace Cad.Server.Modulos.Asistencia.Dominio;

// HU47 (plan de asistencia con IA, 7.1) — modo de la admisión automática. Apagada por defecto. En «Sombra» se calcula qué se
// habría admitido sin actuar; en «Activa» se admite. Detener es siempre seguro: el operador puede pausar (pasar a Apagada).
public enum ModoAdmision { Apagada, Sombra, Activa }

// S6-T15 — el interruptor y sus parámetros, en la base (una sola fila) para cambiarlos sin desplegar. Ia:Habilitada (appsettings)
// es otra cosa: dice si hay asistente; este ajuste dice qué puede hacer la admisión automática con lo que sugiere.
public sealed class AjusteIa
{
    public const int IdUnico = 1;

    public int Id { get; private set; } = IdUnico;
    public ModoAdmision Modo { get; private set; } = ModoAdmision.Apagada;
    // 7.3 y 7.4: retraso antes de admitir (el operador puede tomar la revisión mientras tanto), cupo por hora, longitud mínima de
    // la descripción y si se admite también cuando el asistente corrige la categoría.
    public int RetrasoSegundos { get; private set; } = 30;
    public int MaximoPorHora { get; private set; } = 60;
    public int LongitudMinimaDescripcion { get; private set; } = 40;
    public bool PermitirCorregirCategoria { get; private set; }
    // 7.6: freno automático (con al menos N admisiones automáticas en 24 h y más del P % descartadas, pasa a «Sombra»).
    public int FrenoMinimoAdmisiones { get; private set; } = 20;
    public int FrenoPorcentajeDescartes { get; private set; } = 5;
    public DateTimeOffset? CambiadoEn { get; private set; }

    private AjusteIa() { } // EF Core

    public static AjusteIa PorDefecto() => new();

    public ParametrosAdmision Parametros => new(RetrasoSegundos, MaximoPorHora, LongitudMinimaDescripcion, PermitirCorregirCategoria,
        FrenoMinimoAdmisiones, FrenoPorcentajeDescartes);

    public void Cambiar(ModoAdmision modo, ParametrosAdmision parametros, DateTimeOffset ahora)
    {
        Modo = modo;
        (RetrasoSegundos, MaximoPorHora, LongitudMinimaDescripcion, PermitirCorregirCategoria, FrenoMinimoAdmisiones, FrenoPorcentajeDescartes) =
            (parametros.RetrasoSegundos, parametros.MaximoPorHora, parametros.LongitudMinimaDescripcion, parametros.PermitirCorregirCategoria,
             parametros.FrenoMinimoAdmisiones, parametros.FrenoPorcentajeDescartes);
        CambiadoEn = ahora;
    }
}

public sealed record ParametrosAdmision(int RetrasoSegundos, int MaximoPorHora, int LongitudMinimaDescripcion, bool PermitirCorregirCategoria,
    int FrenoMinimoAdmisiones, int FrenoPorcentajeDescartes)
{
    // Rangos técnicos para que un error de tipeo no deje un valor absurdo (p. ej. un retraso de un día).
    public static readonly (int Minimo, int Maximo) RangoRetraso = (5, 600);
    public static readonly (int Minimo, int Maximo) RangoMaximoPorHora = (1, 500);
    public static readonly (int Minimo, int Maximo) RangoLongitud = (0, 500);
    public static readonly (int Minimo, int Maximo) RangoFrenoMinimo = (1, 1000);
    public static readonly (int Minimo, int Maximo) RangoFrenoPorcentaje = (1, 100);
}

// 7.1 — cada cambio de modo o de parámetros, con quién, cuándo, de qué modo a cuál y por qué. Solo inserción.
public sealed class CambioAjusteIa
{
    public const int MaximoMotivo = 500;

    public Guid Id { get; private set; }
    public ModoAdmision ModoAnterior { get; private set; }
    public ModoAdmision ModoNuevo { get; private set; }
    // Resumen legible de los parámetros vigentes tras el cambio.
    public string Parametros { get; private set; } = "";
    public string Motivo { get; private set; } = "";
    public Guid ActorId { get; private set; }
    public DateTimeOffset En { get; private set; }

    private CambioAjusteIa() { } // EF Core

    public static CambioAjusteIa Registrar(ModoAdmision anterior, ModoAdmision nuevo, ParametrosAdmision parametros, string motivo, Guid actorId,
        DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(),
        ModoAnterior = anterior,
        ModoNuevo = nuevo,
        Parametros = $"retraso {parametros.RetrasoSegundos} s; hasta {parametros.MaximoPorHora} por hora; descripción de {parametros.LongitudMinimaDescripcion}+ caracteres; " +
            $"{(parametros.PermitirCorregirCategoria ? "puede" : "no puede")} corregir la categoría; freno con {parametros.FrenoMinimoAdmisiones}+ admisiones y más del {parametros.FrenoPorcentajeDescartes} % descartadas",
        Motivo = motivo,
        ActorId = actorId,
        En = ahora,
    };
}
