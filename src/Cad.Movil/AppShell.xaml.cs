using Cad.Movil.Servicios;
using Cad.Movil.Vistas;
using Cad.Movil.VistasModelo;

namespace Cad.Movil;

public partial class AppShell : Shell
{
    public AppShell(IAlmacenSesion sesiones)
    {
        InitializeComponent();
        Routing.RegisterRoute(Rutas.Registro, typeof(RegistroPagina));
#if DEBUG
        Routing.RegisterRoute(Rutas.Diagnostico, typeof(DiagnosticoPagina)); // SPIKE01
#endif

        // Con una sesión vigente guardada, se entra directamente al modo del rol.
        Loaded += async (_, _) =>
        {
            if (await sesiones.LeerAsync() is { } sesion && sesion.Vigente(DateTimeOffset.UtcNow))
                await GoToAsync(Rutas.InicioDe(sesion.Rol));
        };
    }
}
