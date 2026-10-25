using System.Globalization;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Acompanamiento.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Acompanamientos;

// HU38 — Consultar los acompañamientos activos y sus posiciones (S4-T09, S4-T10). Solo el operador autorizado (DP04), también
// ante una solicitud directa (CA05). Página propia, separada de los SOS y los reportes (CA04).
// Rediseño (25/10/2026): la vigilancia es la central en dos modos sobre un mismo mapa. «Todas» muestra a cada vigilado con una
// cola por urgencia; con ?sesion=… se sigue a una sola persona: su recorrido aislado y su ficha con la siguiente acción.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class IndexModel(ConsultarAcompanamientos consultar, ConsultarAvisos consultarAvisos, IConfiguration configuracion, TimeProvider reloj) : PageModel
{
    public ListaAcompanamientos Lista { get; private set; } = null!;

    // Sesión que se sigue (modo «sesión»); null en el modo «todas».
    [BindProperty(SupportsGet = true)]
    public Guid? Sesion { get; set; }

    public SeguimientoSesion? Seguimiento { get; private set; }

    [TempData]
    public string? Mensaje { get; set; }

    public string CentroLatitud => Numero("Panel:Mapa:CentroLatitud", -16.3988);
    public string CentroLongitud => Numero("Panel:Mapa:CentroLongitud", -71.5369);
    public string Zoom => Numero("Panel:Mapa:Zoom", 12);
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Lista = await ListaAsync(ct);
        if (Sesion is { } id)
            Seguimiento = await SeguimientoAsync(id, ct); // una sesión que ya no existe deja la página en «todas»
    }

    // Ficha de la sesión que se sigue: la página la pide al elegir a alguien y al llegar algo de esa persona.
    public async Task<IActionResult> OnGetSesionAsync(Guid id, CancellationToken ct) =>
        await SeguimientoAsync(id, ct) is { } seguimiento ? Partial("_SesionSeguimiento", seguimiento) : NotFound();

    private async Task<SeguimientoSesion?> SeguimientoAsync(Guid id, CancellationToken ct) =>
        await consultar.DetalleAsync(id, ct) is { } detalle
            ? new SeguimientoSesion(detalle, await consultarAvisos.DeSesionAsync(id, ct), Formato.Zona(configuracion), consultar.Umbral,
                reloj.GetUtcNow(), Guid.NewGuid(), Mensaje)
            : null;

    // Fragmento que la página vuelve a pedir cada pocos segundos: la antigüedad y el estado cambian aunque no llegue nada.
    public async Task<IActionResult> OnGetListaAsync(CancellationToken ct) => Partial("_ListaAcompanamientos", await ListaAsync(ct));

    private async Task<ListaAcompanamientos> ListaAsync(CancellationToken ct) =>
        new(await consultar.ActivasAsync(ct), Formato.Zona(configuracion), consultar.Umbral, await consultarAvisos.AbiertosAsync(ct));

    private string Numero(string clave, double porDefecto) => configuracion.GetValue(clave, porDefecto).ToString(CultureInfo.InvariantCulture);
}

// HU42/HU43: los avisos sin resolver se muestran con la lista, separados de los SOS (HU42-CA04). Rediseño (25/10/2026): cada
// aviso va en la tarjeta de su sesión, y la lista se ordena por urgencia: primero quien requiere atención, con su motivo.
public sealed record ListaAcompanamientos(IReadOnlyList<AcompanamientoActivo> Sesiones, TimeZoneInfo ZonaHoraria, TimeSpan Umbral,
    IReadOnlyList<AvisoVista>? Avisos = null)
{
    public IReadOnlyList<AvisoVista> AvisosDe(Guid sesionId) => Avisos?.Where(a => a.SesionId == sesionId).ToList() ?? [];

    // Avisos abiertos de sesiones que ya no están activas: no tienen tarjeta, pero siguen pendientes de revisar.
    public IReadOnlyList<AvisoVista> AvisosSinSesion => Avisos?.Where(a => Sesiones.All(s => s.Id != a.SesionId)).ToList() ?? [];

    public Atencion AtencionDe(AcompanamientoActivo s) =>
        s.UltimaConfirmacion is { Respuesta: Modulos.Acompanamiento.Dominio.RespuestaBienestar.NecesitoAyuda, SosEnCurso: true } ? Atencion.PideAyuda
        : AvisosDe(s.Id).Count > 0 ? Atencion.Aviso
        : s.SinConexion ? Atencion.SinConexion
        : s.Ultima is { Desactualizada: true } ? Atencion.Desactualizada
        : Atencion.Calma;

    // Primero lo más urgente; a igual urgencia, la que lleva más tiempo activa.
    public IReadOnlyList<AcompanamientoActivo> Ordenadas =>
        Sesiones.OrderByDescending(AtencionDe).ThenBy(s => s.IniciadaEn).ToList();

    public int RequierenAtencion => Sesiones.Count(s => AtencionDe(s) != Atencion.Calma);
}

public enum Atencion { Calma, Desactualizada, SinConexion, Aviso, PideAyuda }

// Modo «sesión»: la ficha de la persona que se sigue, con lo necesario para decidir la siguiente acción.
public sealed record SeguimientoSesion(DetalleAcompanamiento Sesion, IReadOnlyList<AvisoVista> Avisos, TimeZoneInfo ZonaHoraria, TimeSpan Umbral,
    DateTimeOffset Ahora, Guid IdSolicitudConfirmacion, string? Mensaje)
{
    public PosicionVista? Ultima => Sesion.Posiciones.Count > 0 ? Sesion.Posiciones[0] : null;
    public bool PosicionActual => Sesion.Activa && Ultima is { Desactualizada: false } && !Sesion.SinConexion;
    public ConfirmacionVista? UltimaConfirmacion => Sesion.Confirmaciones is { Count: > 0 } c ? c[0] : null;
    public AvisoVista? AvisoAbierto => Avisos.FirstOrDefault(a => a.Estado != Modulos.Acompanamiento.Dominio.EstadoAviso.Resuelto);
    public bool PideAyuda => UltimaConfirmacion is { Respuesta: Modulos.Acompanamiento.Dominio.RespuestaBienestar.NecesitoAyuda, SosEnCurso: true };
}

public static class Formato
{
    public static TimeZoneInfo Zona(IConfiguration configuracion) =>
        TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");

    public static string Hora(DateTimeOffset momento, TimeZoneInfo zona, string formato = "HH:mm:ss") =>
        TimeZoneInfo.ConvertTime(momento, zona).ToString(formato, CultureInfo.InvariantCulture);

    // HU38-CA02: «hace 12 s», «hace 3 min 5 s».
    public static string Antiguedad(TimeSpan antiguedad) => antiguedad.TotalSeconds < 60
        ? $"hace {(int)antiguedad.TotalSeconds} s"
        : $"hace {(int)antiguedad.TotalMinutes} min {antiguedad.Seconds} s";

    public static string Numero(double valor, string formato = "0.000000") => valor.ToString(formato, CultureInfo.InvariantCulture);
}
