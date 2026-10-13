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

    public async Task OnGetAsync(CancellationToken ct) => Cuentas = await consultar.ListarAsync(ct);
}
