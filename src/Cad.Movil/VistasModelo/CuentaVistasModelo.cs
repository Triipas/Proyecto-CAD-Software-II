using Cad.Contratos.Cuentas;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

public static class Rutas
{
    // ADR-08: la raíz es la bienvenida del ciudadano (celular y PIN); el sereno entra por «¿Eres serenazgo?» (DP26).
    public const string Ingreso = "//ingreso";
    public const string IngresoSereno = "ingreso-sereno";
    public const string Codigo = "codigo";
    public const string CrearCuenta = "crear-cuenta";
    public const string Pin = "pin";
    public const string Diagnostico = "diagnostico"; // SPIKE01, solo Debug
    public const string Ciudadano = "//ciudadano";
    public const string Sereno = "//sereno";
    public const string Reporte = "reporte"; // HU10, desde el inicio del ciudadano
    public const string MisCasos = "//ciudadano/mis-casos"; // HU30, pestaña de la barra inferior
    public const string MiCaso = "mi-caso"; // HU30, con ?id=
    public const string ParametroCalificar = "calificar"; // HU32: «mi-caso?id=…&calificar=1» abre con la evaluación a la vista
    public const string Notificaciones = "//ciudadano/notificaciones"; // HU31, pestaña «Avisos»
    public const string Acompanamiento = "acompanamiento"; // HU34–HU37
    public const string Perfil = "//ciudadano/perfil"; // ADR-08, fase D
    public const string MisDatos = "mis-datos";
    public const string CambiarContrasena = "cambiar-contrasena"; // personal, 22/10/2026

    // Cada rol entra en su modo; el servidor solo emite tokens de la app a ciudadano y sereno (HU02).
    public static string InicioDe(string rol) => rol == "Sereno" ? Sereno : Ciudadano;
}

// HU02 — Iniciar sesión con correo y contraseña: el sereno (DP26, «¿Eres serenazgo?») y las cuentas de ciudadano anteriores
// a ADR-08.
public sealed class IngresoVistaModelo : VistaModeloBase
{
    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly IConfiguracionServidor servidor;
    private readonly INavegacion navegacion;
    private readonly EmisorSos? emisor;
    private string correo = "", contrasena = "", direccionServidor, mensaje = "";

    private readonly IngresoPendiente? pendiente;

    public IngresoVistaModelo(IApiCad api, IAlmacenSesion sesiones, IConfiguracionServidor servidor, INavegacion navegacion, EmisorSos? emisor = null,
        IngresoPendiente? pendiente = null)
    {
        (this.api, this.sesiones, this.servidor, this.navegacion) = (api, sesiones, servidor, navegacion);
        this.emisor = emisor;
        this.pendiente = pendiente;
        direccionServidor = servidor.Direccion?.ToString() ?? "";
        Ingresar = new ComandoAsincrono(IngresarAsync);
        Volver = new ComandoAsincrono(() => navegacion.IrAAsync(".."));
    }

    public string Correo { get => correo; set => Asignar(ref correo, value); }
    public string Contrasena { get => contrasena; set => Asignar(ref contrasena, value); }
    public string DireccionServidor { get => direccionServidor; set => Asignar(ref direccionServidor, value); }
    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => Mensaje.Length > 0;

    public ComandoAsincrono Ingresar { get; }
    public ComandoAsincrono Volver { get; }
    private async Task IngresarAsync()
    {
        Mensaje = "";
        if (servidor.Guardar(DireccionServidor) is { } errorServidor) { Mensaje = errorServidor; return; }
        if (string.IsNullOrWhiteSpace(Correo) || string.IsNullOrEmpty(Contrasena)) { Mensaje = "Escribe tu correo y tu contraseña."; return; }

        var resultado = await api.IniciarSesionAsync(new SolicitudInicioSesion(Correo, Contrasena), CancellationToken.None);
        Contrasena = "";
        switch (resultado)
        {
            case ResultadoApi<RespuestaToken>.Correcto correcto:
                // Contraseña provisional del administrador: primero se cambia. La sesión no se guarda hasta entonces, así que
                // cerrar la app a mitad obliga a ingresar de nuevo.
                if (correcto.Valor.DebeCambiarContrasena && pendiente is not null)
                {
                    pendiente.Token = correcto.Valor;
                    await navegacion.IrAAsync(Rutas.CambiarContrasena);
                    break;
                }
                if (emisor is not null) await emisor.ReiniciarAsync(); // nada del SOS de otra cuenta se reintenta con esta sesión
                await sesiones.GuardarAsync(new SesionApp(correcto.Valor.Token, correcto.Valor.Rol, correcto.Valor.ExpiraEn));
                await navegacion.IrAAsync(Rutas.InicioDe(correcto.Valor.Rol));
                break;
            case ResultadoApi<RespuestaToken>.Rechazado rechazo:
                Mensaje = rechazo.Mensaje; // mismos mensajes que el servidor (HU02-CA02, CA03)
                break;
            case ResultadoApi<RespuestaToken>.SinRespuesta sinRespuesta:
                Mensaje = sinRespuesta.Motivo;
                break;
        }
    }
}
