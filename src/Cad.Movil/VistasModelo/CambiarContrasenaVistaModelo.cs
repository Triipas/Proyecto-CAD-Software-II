using Cad.Contratos.Cuentas;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// El ingreso del sereno con la contraseña provisional del administrador: solo vive en memoria hasta cambiarla.
public sealed class IngresoPendiente
{
    public RespuestaToken? Token { get; set; }
}

// Personal (22/10/2026): el sereno cambia la contraseña que le fijó el administrador antes de operar. El servidor rechaza
// sus demás solicitudes hasta entonces; al cambiarla responde con un token nuevo, que es el que se guarda.
public sealed class CambiarContrasenaVistaModelo : VistaModeloBase
{
    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private readonly IngresoPendiente pendiente;
    private string actual = "", nueva = "", confirmacion = "", mensaje = "";
    private IReadOnlyDictionary<string, string[]> errores = new Dictionary<string, string[]>();

    public CambiarContrasenaVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, IngresoPendiente pendiente)
    {
        (this.api, this.sesiones, this.navegacion, this.pendiente) = (api, sesiones, navegacion, pendiente);
        Guardar = new ComandoAsincrono(GuardarAsync);
        Salir = new ComandoAsincrono(async () => { pendiente.Token = null; await navegacion.IrAAsync(Rutas.Ingreso); });
    }

    public string Actual { get => actual; set => Asignar(ref actual, value); }
    public string Nueva { get => nueva; set => Asignar(ref nueva, value); }
    public string Confirmacion { get => confirmacion; set => Asignar(ref confirmacion, value); }
    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => mensaje.Length > 0;

    public string ErrorActual => Error("actual");
    public string ErrorNueva => Error("nueva");
    public string ErrorConfirmacion => Error("confirmacion");
    public bool HayErrorActual => ErrorActual.Length > 0;
    public bool HayErrorNueva => ErrorNueva.Length > 0;
    public bool HayErrorConfirmacion => ErrorConfirmacion.Length > 0;

    public ComandoAsincrono Guardar { get; }
    public ComandoAsincrono Salir { get; }

    private async Task GuardarAsync()
    {
        Mensaje = "";
        MostrarErrores(new Dictionary<string, string[]>());
        if (pendiente.Token is not { } ingreso)
        {
            Mensaje = "Vuelve a ingresar con tu correo y tu contraseña.";
            return;
        }

        var resultado = await api.CambiarContrasenaAsync(ingreso.Token, new SolicitudCambioContrasena(Actual, Nueva, Confirmacion), CancellationToken.None);
        switch (resultado)
        {
            case ResultadoApi<RespuestaToken>.Correcto correcto:
                (Actual, Nueva, Confirmacion) = ("", "", "");
                pendiente.Token = null;
                await sesiones.GuardarAsync(new SesionApp(correcto.Valor.Token, correcto.Valor.Rol, correcto.Valor.ExpiraEn));
                await navegacion.IrAAsync(Rutas.InicioDe(correcto.Valor.Rol));
                break;
            case ResultadoApi<RespuestaToken>.Rechazado { Estado: 400 } rechazo:
                MostrarErrores(rechazo.Errores);
                Mensaje = "Revisa los datos marcados.";
                break;
            case ResultadoApi<RespuestaToken>.Rechazado:
                // La sesión provisional ya no vale (por ejemplo, el administrador desactivó la cuenta).
                pendiente.Token = null;
                Mensaje = "Tu sesión terminó. Vuelve a ingresar.";
                break;
            case ResultadoApi<RespuestaToken>.SinRespuesta sinRespuesta:
                Mensaje = sinRespuesta.Motivo;
                break;
        }
    }

    private string Error(string campo) => errores.TryGetValue(campo, out var m) && m.Length > 0 ? m[0] : "";

    private void MostrarErrores(IReadOnlyDictionary<string, string[]> nuevos)
    {
        errores = new Dictionary<string, string[]>(nuevos, StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { nameof(ErrorActual), nameof(ErrorNueva), nameof(ErrorConfirmacion),
                     nameof(HayErrorActual), nameof(HayErrorNueva), nameof(HayErrorConfirmacion) })
            Notificar(p);
    }
}
