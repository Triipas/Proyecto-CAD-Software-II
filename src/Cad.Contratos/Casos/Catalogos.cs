namespace Cad.Contratos.Casos;

// Catálogos de registro para la analítica (27/10/2026, decisión del PO). Lo que antes era solo texto libre (el resultado
// del cierre, los motivos, el destino de una derivación) se elige ahora de una lista y se puede contar. El texto libre se
// conserva como detalle. El servidor guarda el código; la app y el panel muestran el texto. Los códigos no cambian: un
// texto se puede reescribir sin tocar los datos guardados.
public sealed record OpcionCatalogo(string Codigo, string Texto);

public static class Catalogo
{
    public static bool Contiene(IReadOnlyList<OpcionCatalogo> opciones, string? codigo) =>
        codigo is not null && opciones.Any(o => o.Codigo == codigo);

    public static string? Texto(IReadOnlyList<OpcionCatalogo> opciones, string? codigo) =>
        opciones.FirstOrDefault(o => o.Codigo == codigo)?.Texto;
}

// HU24 — resultado de la atención al cerrar el caso: si sirvió y cómo terminó.
public static class ResultadosAtencion
{
    public const string ResueltoEnElLugar = "ResueltoEnElLugar";
    public const string EntregadoAPnp = "EntregadoAPnp";
    public const string Derivado = "Derivado";
    public const string SinHallazgo = "SinHallazgo";
    public const string FalsaAlarma = "FalsaAlarma";

    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new(ResueltoEnElLugar, "Resuelto en el lugar"),
        new(EntregadoAPnp, "Persona intervenida o entregada a la PNP"),
        new(Derivado, "Derivado a otra entidad"),
        new(SinHallazgo, "No se encontró el hecho o no había nadie"),
        new(FalsaAlarma, "Falsa alarma"),
    ];
}

// HU13 — prioridad que asigna el operador al clasificar un SOS o al admitir un reporte.
public static class PrioridadesCaso
{
    public const string Alta = "Alta";
    public const string Media = "Media";
    public const string Baja = "Baja";

    public static readonly IReadOnlyList<OpcionCatalogo> Todas =
    [
        new(Alta, "Alta: riesgo para la vida o la integridad"),
        new(Media, "Media: delito o conflicto en curso"),
        new(Baja, "Baja: molestia o hecho ya pasado"),
    ];
}

// HU13 — si hay personas heridas, junto con la prioridad. «No se sabe» es una respuesta válida: el operador no lo inventa.
public static class PersonasHeridas
{
    public const string Si = "Si";
    public const string No = "No";
    public const string NoSeSabe = "NoSeSabe";

    public static readonly IReadOnlyList<OpcionCatalogo> Todas =
    [
        new(Si, "Sí, hay personas heridas"),
        new(No, "No hay personas heridas"),
        new(NoSeSabe, "No se sabe"),
    ];
}

// Descarte de un caso por la central (HU17 y el descarte durante la atención): por qué no se atiende.
public static class MotivosDescarte
{
    public const string FalsoOMalintencionado = "FalsoOMalintencionado";
    public const string Duplicado = "Duplicado";
    public const string NoVerificable = "NoVerificable";
    public const string FueraDeAlcance = "FueraDeAlcance";
    public const string ActivacionAccidental = "ActivacionAccidental";
    public const string Otro = "Otro";

    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new(FalsoOMalintencionado, "Falso o malintencionado"),
        new(Duplicado, "Duplicado de otro caso"),
        new(NoVerificable, "No se pudo verificar"),
        new(FueraDeAlcance, "No es de seguridad ciudadana"),
        new(ActivacionAccidental, "Activación accidental"),
        new(Otro, "Otro motivo"),
    ];
}

// HU09 — por qué el ciudadano cancela su SOS o retira su reporte. Con «Otro», el ciudadano lo escribe.
public static class MotivosCancelacion
{
    public const string PorError = "PorError";
    public const string YaNoLoNecesito = "YaNoLoNecesito";
    public const string MeAyudaronOtros = "MeAyudaronOtros";
    public const string Otro = "Otro";

    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new(PorError, "Lo envié por error"),
        new(YaNoLoNecesito, "Ya estoy bien o ya no lo necesito"),
        new(MeAyudaronOtros, "Me ayudó otra persona o entidad"),
        new(Otro, "Otro motivo"),
    ];
}

// Reasignación a otro sereno (24/10/2026): por qué se cambió.
public static class MotivosReasignacion
{
    public const string SerenoNoResponde = "SerenoNoResponde";
    public const string SerenoOcupado = "SerenoOcupado";
    public const string OtroMasCercano = "OtroMasCercano";
    public const string FallaDelEquipo = "FallaDelEquipo";
    public const string Otro = "Otro";

    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new(SerenoNoResponde, "El sereno no responde"),
        new(SerenoOcupado, "El sereno está ocupado en otro hecho"),
        new(OtroMasCercano, "Hay otro sereno más cerca"),
        new(FallaDelEquipo, "Falla del equipo o sin señal"),
        new(Otro, "Otro motivo"),
    ];
}

// HU16 — a quién se deriva un reporte (antes, texto libre: DP21).
public static class DestinosDerivacion
{
    public const string Pnp = "Pnp";
    public const string Fiscalia = "Fiscalia";
    public const string Cem = "Cem";
    public const string Samu = "Samu";
    public const string Bomberos = "Bomberos";
    public const string Municipalidad = "Municipalidad";
    public const string Otro = "Otro";

    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new(Pnp, "Policía Nacional (PNP)"),
        new(Fiscalia, "Ministerio Público (Fiscalía)"),
        new(Cem, "Centro Emergencia Mujer (Línea 100)"),
        new(Samu, "SAMU (106)"),
        new(Bomberos, "Bomberos (116)"),
        new(Municipalidad, "Otra área de la municipalidad"),
        new(Otro, "Otra entidad"),
    ];
}

// Por dónde llegó un reporte. La app es su propio canal; los demás los registra la central (23/10/2026).
public static class CanalesEntrada
{
    public const string App = "App";
    public const string Llamada = "Llamada";
    public const string Radio = "Radio";
    public const string Presencial = "Presencial";
    public const string RedesSociales = "RedesSociales";
    public const string Otro = "Otro";

    // Los que puede elegir la central al registrar un reporte (la app no es uno de ellos).
    public static readonly IReadOnlyList<OpcionCatalogo> DeLaCentral =
    [
        new(Llamada, "Llamada telefónica"),
        new(Radio, "Radio"),
        new(Presencial, "En persona"),
        new(RedesSociales, "Redes sociales o mensajería"),
        new(Otro, "Otro canal"),
    ];

    public static readonly IReadOnlyList<OpcionCatalogo> Todos = [new(App, "App CAD Móvil"), .. DeLaCentral];
}
