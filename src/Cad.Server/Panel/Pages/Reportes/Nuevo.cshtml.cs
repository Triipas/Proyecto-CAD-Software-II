using System.Globalization;
using Cad.Contratos.Reportes;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Casos.Aplicacion;
using Cad.Server.Modulos.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Reportes;

// Reporte registrado por la central (23/10/2026): p. ej., alguien llamó para avisar de un hecho. Sin ciudadano de la app, con
// quién avisó si lo dio, y ya admitido para asignarlo. Los SOS solo los emiten los ciudadanos desde la app. Solo operadores.
[Authorize(Policy = Politicas.OperadorAutorizado)]
public class NuevoModel(RegistrarReporte registrar, ConsultarCategorias consultarCategorias, IConfiguration configuracion) : PageModel
{
    [BindProperty]
    public FormularioReporte Reporte { get; set; } = new();

    public IReadOnlyList<CategoriaVista> Categorias { get; private set; } = [];
    public Dictionary<string, string[]> Errores { get; private set; } = [];

    [TempData]
    public string? Mensaje { get; set; }

    [TempData]
    public string? DetalleMensaje { get; set; }

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public TimeZoneInfo ZonaHoraria => TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");
    public string CentroLatitud => Numero("Panel:Mapa:CentroLatitud", -16.3988);
    public string CentroLongitud => Numero("Panel:Mapa:CentroLongitud", -71.5369);
    public string UrlTeselas => configuracion["Panel:Mapa:UrlTeselas"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    // Hora local de la central, para el máximo del campo de fecha y hora y su valor inicial.
    public string AhoraLocal => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ZonaHoraria).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Categorias = await consultarCategorias.EjecutarAsync(ct);
        Reporte = new FormularioReporte { IdSolicitud = Guid.NewGuid(), OcurridoEn = AhoraLocal };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var resultado = await registrar.RegistrarDeLaCentralAsync(User.IdUsuario(), Solicitud(), Reporte.ContactoNombre, Reporte.ContactoTelefono, ct);
        switch (resultado)
        {
            case ResultadoRegistroReporte.Registrado or ResultadoRegistroReporte.YaRegistrado:
                var caso = resultado is ResultadoRegistroReporte.Registrado r ? r.Caso : ((ResultadoRegistroReporte.YaRegistrado)resultado).Caso;
                Mensaje = "Reporte registrado y admitido.";
                DetalleMensaje = "Ya aparece en «Reportes admitidos» de la central: asígnale un sereno.";
                return RedirectToPage("/Reportes/Detalle", new { id = caso.Id }); // PRG
            default:
                Errores = ((ResultadoRegistroReporte.Invalido)resultado).Errores;
                Categorias = await consultarCategorias.EjecutarAsync(ct);
                return Page();
        }
    }

    // Con un punto en el mapa, la ubicación es ese punto (y la referencia la acompaña); sin punto, la referencia.
    private SolicitudRegistroReporte Solicitud()
    {
        DateTimeOffset? ocurrido = DateTime.TryParseExact(Reporte.OcurridoEn, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(local, ZonaHoraria.GetUtcOffset(local))
            : null;
        var latitud = Decimal(Reporte.Latitud);
        var longitud = Decimal(Reporte.Longitud);
        var ubicacion = latitud is not null && longitud is not null
            ? new UbicacionHecho(FuentesUbicacionHecho.Mapa, latitud, longitud, null, Reporte.Referencia)
            : new UbicacionHecho(FuentesUbicacionHecho.Texto, null, null, null, Reporte.Referencia);
        return new SolicitudRegistroReporte(Reporte.IdSolicitud, Reporte.CategoriaId, Reporte.Descripcion, ocurrido, ubicacion);
    }

    // El mapa escribe las coordenadas con punto decimal, sea cual sea la cultura del servidor.
    private static double? Decimal(string? texto) =>
        double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor) ? valor : null;

    private string Numero(string clave, double porDefecto) =>
        configuracion.GetValue(clave, porDefecto).ToString(CultureInfo.InvariantCulture);

    public sealed class FormularioReporte
    {
        public Guid IdSolicitud { get; set; }
        public Guid? CategoriaId { get; set; }
        public string? Descripcion { get; set; }
        // «yyyy-MM-ddTHH:mm» en la hora de la central (campo datetime-local).
        public string? OcurridoEn { get; set; }
        public string? Latitud { get; set; }
        public string? Longitud { get; set; }
        public string? Referencia { get; set; }
        public string? ContactoNombre { get; set; }
        public string? ContactoTelefono { get; set; }
    }
}
