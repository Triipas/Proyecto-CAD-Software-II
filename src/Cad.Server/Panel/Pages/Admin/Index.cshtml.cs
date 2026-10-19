using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Admin;

// HU04 (base): inicio del administrador. Rediseño CAD v2: su única tarea son las cuentas institucionales, así que el inicio
// las muestra directamente, con una línea de resumen por rol y estado.
[Authorize(Policy = Politicas.Administrador)]
public class IndexModel(ConsultarCuentasInstitucionales consultar) : PageModel
{
    public IReadOnlyList<CuentaInstitucional> Cuentas { get; private set; } = [];
    public int Operadores => Cuentas.Count(c => c.Rol == "Operador");
    public int Serenos => Cuentas.Count(c => c.Rol == "Sereno");
    public int Activas => Cuentas.Count(c => c.Activa);
    public int Desactivadas => Cuentas.Count - Activas;

    public async Task OnGetAsync(CancellationToken ct) => Cuentas = await consultar.ListarAsync(ct);
}
