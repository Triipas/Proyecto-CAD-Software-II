using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages.Admin.Cuentas;

// HU04-CA02, CA05, CA07: modificación de los datos permitidos de una cuenta institucional. HU05: desactivación.
[Authorize(Policy = Politicas.Administrador)]
public class EditarModel(ConsultarCuentasInstitucionales consultar, ModificarCuentaInstitucional modificar,
    DesactivarCuentaInstitucional desactivar) : PageModel
{
    [BindProperty]
    public FormularioModificacion Formulario { get; set; } = new();

    public CuentaInstitucional Cuenta { get; private set; } = null!;

    public Dictionary<string, string[]> Errores { get; private set; } = [];

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(id, ct) is not { } cuenta)
            return NotFound();

        Cuenta = cuenta;
        Formulario = new FormularioModificacion
        {
            Nombres = cuenta.Nombres,
            Apellidos = cuenta.Apellidos,
            Correo = cuenta.Correo,
            Telefono = cuenta.Telefono,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var resultado = await modificar.EjecutarAsync(id, new DatosModificacionCuenta(
            Formulario.Nombres, Formulario.Apellidos, Formulario.Correo, Formulario.Telefono, Formulario.NuevaContrasena), ct);
        Formulario.NuevaContrasena = null;

        if (resultado is ResultadoCuentaInstitucional.NoEncontrada || await consultar.ObtenerAsync(id, ct) is not { } cuenta)
            return NotFound();

        Cuenta = cuenta;
        switch (resultado)
        {
            case ResultadoCuentaInstitucional.Correcto:
                TempData[nameof(IndexModel.Mensaje)] = $"Se guardaron los cambios de la cuenta de {cuenta.Nombres} {cuenta.Apellidos}.";
                return RedirectToPage("/Admin/Cuentas/Index");
            case ResultadoCuentaInstitucional.Invalido invalido:
                Errores = invalido.Errores;
                return Page();
            default:
                Errores = new() { ["correo"] = ["Ya existe otra cuenta con ese correo electrónico."] };
                return Page();
        }
    }

    // HU05-CA01: la cuenta pasa a «Desactivada». CA05: solo el administrador (política de la página).
    public async Task<IActionResult> OnPostDesactivarAsync(Guid id, CancellationToken ct)
    {
        if (await desactivar.EjecutarAsync(id, ct) is not ResultadoCuentaInstitucional.Correcto
            || await consultar.ObtenerAsync(id, ct) is not { } cuenta)
            return NotFound();

        TempData[nameof(IndexModel.Mensaje)] =
            $"Se desactivó la cuenta de {cuenta.Nombres} {cuenta.Apellidos}: ya no puede iniciar sesión. Sus registros se conservan.";
        return RedirectToPage("/Admin/Cuentas/Index");
    }

    public sealed class FormularioModificacion
    {
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string Correo { get; set; } = "";
        public string? Telefono { get; set; }
        public string? NuevaContrasena { get; set; }
    }
}
