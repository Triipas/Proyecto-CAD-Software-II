using Cad.Contratos.Casos;
using Cad.Server.Modulos.Analitica;

namespace Cad.Server.Panel.Pages.Analitica;

// «Reasignaciones» como «qué hay que arreglar» (16/11/2026): cada motivo apunta a algo distinto que se puede revisar. Aquí, lo que
// dice cada uno (a quién le toca, qué revisar, cómo se nombra en el titular) y su color, el mismo en las columnas y en su tarjeta.
public static class Arreglar
{
    public sealed record Motivo(string Quien, string Revisar, string Titular, string Color);

    private static readonly IReadOnlyDictionary<string, Motivo> PorCodigo = new Dictionary<string, Motivo>
    {
        [MotivosReasignacion.SerenoNoResponde] = new("El aviso al sereno", "Revisar si le llega el aviso y cómo confirma el despacho.", "fueron porque el sereno no respondía", "#F5B544"),
        [MotivosReasignacion.OtroMasCercano] = new("La asignación", "Revisar si se usa la sugerencia del sereno más cercano al asignar.", "fueron porque había otro sereno más cerca", "#45E3A4"),
        [MotivosReasignacion.SerenoOcupado] = new("La disponibilidad", "Puede faltar gente en esas horas: cruzar con «Cobertura por zona».", "fueron porque el sereno estaba ocupado en otro hecho", "#7FB2F0"),
        [MotivosReasignacion.FallaDelEquipo] = new("La app o la señal", "Revisar equipos y zonas sin señal: no es un problema de la asignación.", "fueron por una falla del equipo o falta de señal", "#C4B5FD"),
        [MotivosReasignacion.Otro] = new("Otros motivos", "Leer el detalle en el historial de esos casos.", "fueron por otro motivo", "#5FCDA0"),
        [ConsultarReasignaciones.SinMotivo] = new("El registro", "Son anteriores al catálogo de motivos (27/10/2026): desde entonces siempre se elige uno.", "no tienen motivo registrado", "#8A9DBB"),
    };

    public static Motivo De(string codigo) => PorCodigo.GetValueOrDefault(codigo) ?? new("Otros motivos", "Leer el detalle en el historial de esos casos.", "fueron por otro motivo", "#5FCDA0");

    // «Casi 4 de cada 10 reasignaciones», «La mitad de las reasignaciones», «Todas las reasignaciones»: una parte dicha como se
    // diría en voz alta. La cifra va aparte para destacarla en el titular.
    public static (string Antes, string Cifra, string Despues) Parte(int veces, int total)
    {
        if (total <= 0 || veces <= 0) return ("", "Ninguna", " reasignación");
        var p = (double)veces / total;
        if (veces == total) return ("", "Todas", " las reasignaciones");
        var decimos = (int)Math.Round(p * 10, MidpointRounding.AwayFromZero);
        if (Math.Abs(p - 0.5) < 0.02) return ("", "La mitad", " de las reasignaciones");
        if (p < 0.095) return ("", $"{veces} de las {total}", " reasignaciones"); // menos de 1 de cada 10: mejor la cuenta
        var casi = p * 10 < decimos - 0.05 ? "Casi " : p * 10 > decimos + 0.05 ? "Más de " : "";
        return (casi, $"{decimos} de cada 10", " reasignaciones");
    }
}
