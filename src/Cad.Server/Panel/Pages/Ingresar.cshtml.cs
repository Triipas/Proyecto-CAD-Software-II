using System.Security.Claims;
using Cad.Server.Compartido;
using Cad.Server.Modulos.Cuentas.Aplicacion;
using Cad.Server.Modulos.Cuentas.Dominio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Cad.Server.Panel.Pages;

// HU02 — inicio de sesión del panel (operador y administrador), con cookie HttpOnly.
[AllowAnonymous]
public class IngresarModel(IniciarSesion iniciarSesion) : PageModel
{
    [BindProperty]
    public FormularioIngreso Formulario { get; set; } = new();

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        var rol = User.FindFirstValue(Autenticacion.ReclamoRol);
        return rol is null ? Page() : LocalRedirect(InicioDe(rol));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var resultado = await iniciarSesion.EjecutarAsync(Formulario.Correo, Formulario.Contrasena, Canal.Panel);
        Formulario.Contrasena = "";

        if (resultado is not ResultadoInicioSesion.Exito exito)
        {
            Error = IniciarSesion.Mensaje(resultado);
            return Page();
        }

        var identidad = Autenticacion.CrearIdentidad(exito.Usuario, exito.Rol, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidad));
        return LocalRedirect(InicioDe(exito.Rol));
    }

    public static string InicioDe(string rol) => rol == Roles.Administrador ? "/panel/admin" : "/panel/central";

    public sealed class FormularioIngreso
    {
        public string Correo { get; set; } = "";
        public string Contrasena { get; set; } = "";
    }
}
