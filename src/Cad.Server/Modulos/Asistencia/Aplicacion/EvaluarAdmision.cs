using Cad.Contratos.Casos;
using Cad.Server.Modulos.Asistencia.Dominio;
using Cad.Server.Modulos.Asistencia.Puertos;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Asistencia.Aplicacion;

// Lo que la evaluación necesita del reporte, ya leído por AnalizarReporte.
public sealed record ReporteParaAdmision(Guid CasoId, bool DeLaApp, bool Recibido, Guid CategoriaCiudadanoId, bool ConPunto, int LargoDescripcion);

// HU47 (S6-T16; plan de asistencia con IA, 7.3) — ¿se puede admitir solo este reporte? Todas las condiciones a la vez: modo
// «Sombra» o «Activa»; reporte de la app en «Recibido»; decisión sugerida «Admitir» con confianza alta; sin posible urgencia ni
// duplicado; categoría igual a la del ciudadano (salvo PermitirCorregirCategoria); con punto en el mapa y descripción suficiente;
// segunda opinión independiente que coincide en decisión, categoría y ausencia de urgencia; y dentro del cupo por hora. La segunda
// opinión solo se pide si se cumple todo lo demás: así no se gasta cuota en lo que no se admitiría. «Derivar» y «Descartar» nunca se
// aplican solos (CA10).
public sealed class EvaluarAdmision(CadDbContext db, IAsistenteReportes asistente, ILogger<EvaluarAdmision> registro)
{
    public const string PrefijoMotivo = "Admitido automáticamente por el sistema: ";

    // Las condiciones que no dependen de la segunda opinión ni del cupo; null si se cumplen todas.
    public static CondicionAdmision? Previas(ReporteParaAdmision r, AnalisisReporte a, AjusteIa ajuste)
    {
        if (!r.DeLaApp) return CondicionAdmision.NoEsDeLaApp;
        if (!r.Recibido) return CondicionAdmision.NoRecibido;
        if (a.Decision != DecisionSugerida.Admitir) return CondicionAdmision.DecisionNoAdmitir;
        if (a.Confianza != NivelConfianza.Alta) return CondicionAdmision.ConfianzaNoAlta;
        if (a.PosibleUrgencia || a.Senales.Contains(SenalTriaje.PosibleUrgencia)) return CondicionAdmision.PosibleUrgencia;
        if (a.DuplicadoDeId is not null || a.Senales.Contains(SenalTriaje.Duplicado)) return CondicionAdmision.Duplicado;
        if (a.CategoriaSugeridaId != r.CategoriaCiudadanoId && !ajuste.PermitirCorregirCategoria) return CondicionAdmision.CategoriaDistinta;
        if (!r.ConPunto) return CondicionAdmision.SinPunto;
        if (r.LargoDescripcion < ajuste.LongitudMinimaDescripcion) return CondicionAdmision.DescripcionCorta;
        return null;
    }

    public static bool Coinciden(AnalisisReporte primera, AnalisisReporte segunda) =>
        segunda.Decision == DecisionSugerida.Admitir && segunda.CategoriaSugeridaId == primera.CategoriaSugeridaId
        && !segunda.PosibleUrgencia && segunda.DuplicadoDeId is null;

    // null si el modo está apagado: no hay nada que evaluar.
    public async Task<EvaluacionAdmision?> EjecutarAsync(ReporteParaAdmision reporte, AnalisisReporte analisis, EntradaAnalisis entrada,
        TimeSpan tiempoMaximo, DateTimeOffset ahora, CancellationToken ct)
    {
        var ajuste = await db.AjustesIa.AsNoTracking().SingleOrDefaultAsync(a => a.Id == AjusteIa.IdUnico, ct) ?? AjusteIa.PorDefecto();
        if (ajuste.Modo == ModoAdmision.Apagada)
            return null;

        var condicion = Previas(reporte, analisis, ajuste);
        string? modeloVerificador = null;
        bool? coincide = null;
        if (condicion is null)
        {
            var segunda = await VerificarAsync(entrada, tiempoMaximo, ct);
            modeloVerificador = segunda?.Modelo;
            coincide = segunda is null ? null : Coinciden(analisis, segunda.Analisis);
            condicion = segunda is null ? CondicionAdmision.SinVerificacion : coincide == true ? null : CondicionAdmision.VerificadorDiscrepa;
        }
        // Cupo por hora: las elegibles del mismo modo en la última hora (en «Sombra», las que habría admitido).
        if (condicion is null)
        {
            var desde = ahora.AddHours(-1);
            var enLaHora = await db.EvaluacionesAdmision.CountAsync(e => e.Elegible && e.Modo == ajuste.Modo && e.EvaluadaEn >= desde, ct);
            if (enLaHora >= ajuste.MaximoPorHora)
                condicion = CondicionAdmision.CupoAgotado;
        }

        var prioridad = Catalogo.Contiene(PrioridadesCaso.Todas, analisis.Prioridad) ? analisis.Prioridad : PrioridadesCaso.Media;
        var motivo = PrefijoMotivo + (string.IsNullOrWhiteSpace(analisis.Justificacion) ? analisis.MotivoBorrador : analisis.Justificacion);
        return EvaluacionAdmision.Registrar(reporte.CasoId, ajuste.Modo, condicion, modeloVerificador, coincide, analisis.CategoriaSugeridaId,
            prioridad, motivo, ahora, ajuste.RetrasoSegundos);
    }

    // Con su propio tiempo máximo; si falla, no hay segunda opinión (y no se admite).
    private async Task<ResultadoAsistente?> VerificarAsync(EntradaAnalisis entrada, TimeSpan tiempoMaximo, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(tiempoMaximo);
        try
        {
            var segunda = await asistente.VerificarAsync(entrada, limite.Token);
            return segunda is null || ValidarAnalisis.Aplicar(segunda.Analisis, entrada) is not { } valida ? null : segunda with { Analisis = valida };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registro.LogWarning("Reporte {CasoId}: la segunda opinión no respondió ({Error}).", entrada.CasoId, ex.GetType().Name);
            return null;
        }
    }
}
