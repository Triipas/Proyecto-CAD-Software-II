using Cad.Contratos.Cuentas;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

public static class Rutas
{
    public const string Ingreso = "//ingreso";
    public const string Registro = "registro";
    public const string Ciudadano = "//ciudadano";
    public const string Sereno = "//sereno";

    // Cada rol entra en su modo; el servidor solo emite tokens de la app a ciudadano y sereno (HU02).
    public static string InicioDe(string rol) => rol == "Sereno" ? Sereno : Ciudadano;
}

// HU02 — Iniciar sesión (canal de la app: ciudadano y sereno).
public sealed class IngresoVistaModelo : VistaModeloBase
{
    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly IConfiguracionServidor servidor;
    private readonly INavegacion navegacion;
    private string correo = "", contrasena = "", direccionServidor, mensaje = "";

    public IngresoVistaModelo(IApiCad api, IAlmacenSesion sesiones, IConfiguracionServidor servidor, INavegacion navegacion)
    {
        (this.api, this.sesiones, this.servidor, this.navegacion) = (api, sesiones, servidor, navegacion);
        direccionServidor = servidor.Direccion?.ToString() ?? "";
        Ingresar = new ComandoAsincrono(IngresarAsync);
        IrARegistro = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.Registro));
    }

    public string Correo { get => correo; set => Asignar(ref correo, value); }
    public string Contrasena { get => contrasena; set => Asignar(ref contrasena, value); }
    public string DireccionServidor { get => direccionServidor; set => Asignar(ref direccionServidor, value); }
    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => Mensaje.Length > 0;

    public ComandoAsincrono Ingresar { get; }
    public ComandoAsincrono IrARegistro { get; }

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

// HU01 — Registrar mi cuenta de ciudadano (DP01). El servidor revalida siempre.
public sealed class RegistroVistaModelo : VistaModeloBase
{
    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private string nombres = "", apellidos = "", correo = "", telefono = "", contrasena = "", confirmacion = "", mensaje = "";
    private IReadOnlyDictionary<string, string[]> errores = new Dictionary<string, string[]>();

    public RegistroVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion)
    {
        (this.api, this.sesiones, this.navegacion) = (api, sesiones, navegacion);
        Registrar = new ComandoAsincrono(RegistrarAsync);
        Volver = new ComandoAsincrono(() => navegacion.IrAAsync(".."));
    }

    public string Nombres { get => nombres; set => Asignar(ref nombres, value); }
    public string Apellidos { get => apellidos; set => Asignar(ref apellidos, value); }
    public string Correo { get => correo; set => Asignar(ref correo, value); }
    public string Telefono { get => telefono; set => Asignar(ref telefono, value); }
    public string Contrasena { get => contrasena; set => Asignar(ref contrasena, value); }
    public string ConfirmacionContrasena { get => confirmacion; set => Asignar(ref confirmacion, value); }
    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => Mensaje.Length > 0;

    // Error por campo (claves del servidor: nombres, apellidos, correo, telefono, contrasena).
    public string ErrorNombres => Error("nombres");
    public string ErrorApellidos => Error("apellidos");
    public string ErrorCorreo => Error("correo");
    public string ErrorTelefono => Error("telefono");
    public string ErrorContrasena => Error("contrasena");
    public string ErrorConfirmacion => Error("confirmacion");

    public ComandoAsincrono Registrar { get; }
    public ComandoAsincrono Volver { get; }

    private string Error(string campo) => errores.TryGetValue(campo, out var m) && m.Length > 0 ? m[0] : "";

    private void MostrarErrores(IReadOnlyDictionary<string, string[]> nuevos)
    {
        errores = nuevos;
        foreach (var p in new[] { nameof(ErrorNombres), nameof(ErrorApellidos), nameof(ErrorCorreo), nameof(ErrorTelefono), nameof(ErrorContrasena), nameof(ErrorConfirmacion) })
            Notificar(p);
    }

    private async Task RegistrarAsync()
    {
        Mensaje = "";
        // Confirmar la contraseña es solo una ayuda de la interfaz: el servidor no la recibe.
        if (Contrasena != ConfirmacionContrasena)
        {
            MostrarErrores(new Dictionary<string, string[]> { ["confirmacion"] = ["Las contraseñas no coinciden."] });
            Mensaje = "Revisa los campos marcados.";
            return;
        }

        var resultado = await api.RegistrarAsync(new SolicitudRegistroCiudadano(Nombres, Apellidos, Correo, Telefono, Contrasena), CancellationToken.None);
        switch (resultado)
        {
            case ResultadoApi<RespuestaRegistroCiudadano>.Correcto:
                MostrarErrores(new Dictionary<string, string[]>());
                // La cuenta existe: se inicia sesión con las mismas credenciales.
                if (await api.IniciarSesionAsync(new SolicitudInicioSesion(Correo, Contrasena), CancellationToken.None)
                    is ResultadoApi<RespuestaToken>.Correcto sesion)
                {
                    await sesiones.GuardarAsync(new SesionApp(sesion.Valor.Token, sesion.Valor.Rol, sesion.Valor.ExpiraEn));
                    await navegacion.IrAAsync(Rutas.Ciudadano);
                }
                else
                {
                    await navegacion.IrAAsync(Rutas.Ingreso);
                }
                break;
            case ResultadoApi<RespuestaRegistroCiudadano>.Rechazado rechazo:
                MostrarErrores(rechazo.Errores);
                Mensaje = rechazo.Errores.Count > 0 ? "Revisa los campos marcados." : rechazo.Mensaje; // p. ej. correo ya registrado
                break;
            case ResultadoApi<RespuestaRegistroCiudadano>.SinRespuesta sinRespuesta:
                Mensaje = sinRespuesta.Motivo;
                break;
        }
        Contrasena = ConfirmacionContrasena = "";
    }
}
