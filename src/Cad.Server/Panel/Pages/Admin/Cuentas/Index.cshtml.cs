using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Admin.Cuentas;

// HU04-CA04: listado de cuentas institucionales con su rol y su estado.
[Authorize(Policy = Politicas.Administrador)]
public class IndexModel(ConsultarCuentasInstitucionales consultar) : PageModel
{
    public IReadOnlyList<CuentaInstitucional> Cuentas { get; private set; } = [];

    [TempData]
    public string? Mensaje { get; set; }

    // Búsqueda por código, nombre, correo o DNI completo, y filtro por rol. El listado es corto: se filtra en memoria.
    [BindProperty(SupportsGet = true)]
    public string? Buscar { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Rol { get; set; }

    public int Total { get; private set; }

    public bool Filtrando => !string.IsNullOrWhiteSpace(Buscar) || Rol is not null && RolesInstitucionales.Todos.Contains(Rol);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var todas = await consultar.ListarAsync(ct);
        Total = todas.Count;
        var texto = Buscar?.Trim() ?? "";
        Cuentas = todas
            .Where(c => Rol is null || !RolesInstitucionales.Todos.Contains(Rol) || c.Rol == Rol)
            .Where(c => texto.Length == 0 || Coincide(c, texto))
            .ToList();
    }

    private static bool Coincide(CuentaInstitucional c, string texto) =>
        c.Dni == texto
        || (c.CodigoPersonal ?? "").Contains(texto, StringComparison.OrdinalIgnoreCase)
        || $"{c.Nombres} {c.Apellidos}".Contains(texto, StringComparison.CurrentCultureIgnoreCase)
        || c.Correo.Contains(texto, StringComparison.OrdinalIgnoreCase);
}
