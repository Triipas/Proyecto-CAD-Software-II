using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Admin.Cuentas;

// HU04-CA01, CA02, CA03: alta de una cuenta de operador o sereno.
[Authorize(Policy = Politicas.Administrador)]
public class NuevaModel(CrearCuentaInstitucional crear) : PageModel
{
    [BindProperty]
    public FormularioNuevaCuenta Formulario { get; set; } = new();

    public Dictionary<string, string[]> Errores { get; private set; } = [];

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var resultado = await crear.EjecutarAsync(new DatosNuevaCuenta(
            Formulario.Nombres, Formulario.Apellidos, Formulario.Correo, Formulario.Telefono, Formulario.Rol, Formulario.Contrasena), ct);
        Formulario.Contrasena = "";

        switch (resultado)
        {
            case ResultadoCuentaInstitucional.Correcto:
                TempData[nameof(IndexModel.Mensaje)] =
                    $"Se creó la cuenta de {Formulario.Rol.ToLowerInvariant()} de {Formulario.Nombres.Trim()} {Formulario.Apellidos.Trim()}.";
                return RedirectToPage("/Admin/Cuentas/Index");
            case ResultadoCuentaInstitucional.Invalido invalido:
                Errores = invalido.Errores;
                return Page();
            default:
                Errores = new() { ["correo"] = ["Ya existe una cuenta con ese correo electrónico."] };
                return Page();
        }
    }

    public sealed class FormularioNuevaCuenta
    {
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string Correo { get; set; } = "";
        public string? Telefono { get; set; }
        public string Rol { get; set; } = "";
        public string Contrasena { get; set; } = "";
    }
}
