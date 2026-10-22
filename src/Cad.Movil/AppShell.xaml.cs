using Cad.Movil.Servicios;
using Cad.Movil.Vistas;
using Cad.Movil.VistasModelo;

namespace Cad.Movil;

public partial class AppShell : Shell
{
    public AppShell(IAlmacenSesion sesiones, IAlmacenDispositivo dispositivos, FlujoIngresoCelular flujo)
    {
        InitializeComponent();
        // ADR-08: bienvenida (raíz «ingreso») → código → crea tu cuenta → PIN; el sereno entra por «¿Eres serenazgo?».
        Routing.RegisterRoute(Rutas.IngresoSereno, typeof(IngresoPagina));
        Routing.RegisterRoute(Rutas.Codigo, typeof(CodigoPagina));
        Routing.RegisterRoute(Rutas.CrearCuenta, typeof(CrearCuentaPagina));
        Routing.RegisterRoute(Rutas.Pin, typeof(PinPagina));
        Routing.RegisterRoute(Rutas.MisDatos, typeof(MisDatosPagina));
        Routing.RegisterRoute(Rutas.CambiarContrasena, typeof(CambiarContrasenaPagina)); // personal, 22/10/2026
        Routing.RegisterRoute(Rutas.Reporte, typeof(ReportePagina));
        Routing.RegisterRoute(Rutas.MiCaso, typeof(MiCasoPagina));
        Routing.RegisterRoute(Rutas.Acompanamiento, typeof(AcompanamientoPagina));
#if DEBUG
        Routing.RegisterRoute(Rutas.Diagnostico, typeof(DiagnosticoPagina)); // SPIKE01
#endif

        Loaded += async (_, _) =>
        {
            // Con una sesión vigente guardada, se entra directamente al modo del rol: el SOS nunca espera al PIN.
            if (await sesiones.LeerAsync() is { } sesion && sesion.Vigente(DateTimeOffset.UtcNow))
            {
                await GoToAsync(Rutas.InicioDe(sesion.Rol));
            }
            // Sesión vencida o cerrada en un teléfono ya vinculado: basta el PIN.
            else if (await dispositivos.LeerAsync() is { } vinculado)
            {
                flujo.Reiniciar(vinculado.Celular);
                flujo.Llave = vinculado.Llave;
                flujo.ModoPin = ModoPin.Ingresar;
                await GoToAsync(Rutas.Pin);
            }
        };
    }
}
