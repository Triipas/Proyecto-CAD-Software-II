using System.Security.Claims;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages;

// Cambio de la contraseña propia del operador (22/10/2026). Obligatorio tras el alta o un restablecimiento del
// administrador: hasta hacerlo, las demás páginas del panel lo traen aquí. Al cambiarla se abre una sesión nueva.
[Authorize(Policy = Politicas.Personal)]
public class CambiarContrasenaModel(CambiarContrasena cambiar) : PageModel
{
    [BindProperty]
    public FormularioCambio Formulario { get; set; } = new();

    public Dictionary<string, string[]> Errores { get; private set; } = [];

    public bool Obligatorio => User.DebeCambiarContrasena();

    public string? ErrorDe(string campo) => Errores.TryGetValue(campo, out var mensajes) ? mensajes[0] : null;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var resultado = await cambiar.EjecutarAsync(User.IdUsuario(), Formulario.Actual, Formulario.Nueva, Formulario.Confirmacion);
        Formulario = new();

        if (resultado is ResultadoCambioContrasena.Invalido invalido)
        {
            Errores = invalido.Errores;
            return Page();
        }

        var correcto = (ResultadoCambioContrasena.Correcto)resultado;
        var identidad = Autenticacion.CrearIdentidad(correcto.Usuario, correcto.Rol, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidad));
        TempData["ContrasenaCambiada"] = "Tu contraseña se cambió.";
        return LocalRedirect(IngresarModel.InicioDe(correcto.Rol));
    }

    public sealed class FormularioCambio
    {
        public string Actual { get; set; } = "";
        public string Nueva { get; set; } = "";
        public string Confirmacion { get; set; } = "";
    }
}
