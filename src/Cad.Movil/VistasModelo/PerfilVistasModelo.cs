using System.Globalization;
using Cad.Contratos.Cuentas;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// ADR-08, fase D — pestaña «Perfil» del ciudadano: sus datos, seguridad, privacidad, ayuda y cierre de sesión.
public sealed class PerfilVistaModelo : VistaModeloBase
{
    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private readonly FlujoIngresoCelular flujo;
    private readonly ServicioAcompanamiento? acompanamiento;
    private readonly EmisorSos? emisor;
    private PerfilCiudadanoVista? perfil;
    private string mensaje = "", tipoMensaje = "informacion";

    public PerfilVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, FlujoIngresoCelular flujo,
        ServicioAcompanamiento? acompanamiento = null, EmisorSos? emisor = null)
    {
        (this.api, this.sesiones, this.navegacion, this.flujo, this.acompanamiento) = (api, sesiones, navegacion, flujo, acompanamiento);
        this.emisor = emisor;
        IrAMisDatos = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.MisDatos));
        CambiarPin = new ComandoAsincrono(CambiarPinAsync);
        CerrarSesion = new ComandoAsincrono(CerrarSesionAsync);
    }

    // La pantalla lo sustituye por un diálogo del sistema; en las pruebas responde sí.
    public Func<Task<bool>> ConfirmarCierre { get; set; } = () => Task.FromResult(true);
    // 26/10/2026: con un acompañamiento en curso, el diálogo avisa que se finalizará.
    public Func<Task<bool>> ConfirmarCierreConAcompanamiento { get; set; } = () => Task.FromResult(true);

    public string Iniciales => perfil is null ? "" :
        $"{perfil.Nombres.Trim().FirstOrDefault()}{perfil.Apellidos.Trim().FirstOrDefault()}".ToUpper(CultureInfo.CurrentCulture);
    public string Nombre => perfil is null ? "" : $"{perfil.Nombres} {perfil.Apellidos}";
    public string Celular => perfil is { Celular.Length: 9 } p ? $"+51 {FormatoCelular.Mostrar(p.Celular)}" : perfil?.Celular ?? "";
    public bool CelularVerificado => perfil?.CelularVerificado == true;
    public bool TienePin => perfil?.TienePin == true;

    public string TextoDatos => perfil is null ? "" : perfil.DatosCompletos
        ? "Datos completos"
        : "Faltan datos";
    public bool DatosIncompletos => perfil is not null && !perfil.DatosCompletos;

    public string TextoTerminos => perfil?.TerminosAceptadosEn is { } en
        ? $"Aceptaste los términos (versión {perfil.TerminosVersion}) el {en.ToLocalTime():dd/MM/yyyy}."
        : "Tu cuenta es anterior a los términos actuales.";
    public string Terminos => CrearCuentaVistaModelo.TextoTerminos;

    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public string TipoMensaje { get => tipoMensaje; private set => Asignar(ref tipoMensaje, value); }
    public bool HayMensaje => Mensaje.Length > 0;

    public ComandoAsincrono IrAMisDatos { get; }
    public ComandoAsincrono CambiarPin { get; }
    public ComandoAsincrono CerrarSesion { get; }

    public async Task CargarAsync()
    {
        if (flujo.AvisoPendiente is { } aviso)
        {
            (TipoMensaje, Mensaje) = ("exito", aviso);
            flujo.AvisoPendiente = null;
        }
        if (await sesiones.LeerAsync() is not { } sesion)
            return;
        switch (await api.ConsultarPerfilAsync(sesion.Token, CancellationToken.None))
        {
            case ResultadoApi<PerfilCiudadanoVista>.Correcto correcto:
                perfil = correcto.Valor;
                foreach (var p in new[] { nameof(Iniciales), nameof(Nombre), nameof(Celular), nameof(CelularVerificado), nameof(TienePin),
                             nameof(TextoDatos), nameof(DatosIncompletos), nameof(TextoTerminos) })
                    Notificar(p);
                break;
            case ResultadoApi<PerfilCiudadanoVista>.Rechazado { Estado: 401 }:
                sesiones.Cerrar();
                await navegacion.IrAAsync(Rutas.Ingreso);
                break;
            case ResultadoApi<PerfilCiudadanoVista>.SinRespuesta sinRespuesta when perfil is null:
                (TipoMensaje, Mensaje) = ("advertencia", sinRespuesta.Motivo);
                break;
        }
    }

    private Task CambiarPinAsync()
    {
        Mensaje = "";
        flujo.Reiniciar(perfil?.Celular ?? "");
        flujo.ModoPin = ModoPin.Cambiar;
        return navegacion.IrAAsync(Rutas.Pin);
    }

    // Sin sesión, pedir ayuda vuelve a exigir el PIN: por eso se confirma. 26/10/2026: con un acompañamiento en curso, el
    // diálogo avisa que se finalizará y, si acepta, se finaliza como con «Finalizar acompañamiento» (HU36): deja de compartir
    // su ubicación al instante y el cierre llega a la central, para que no lo vea «Sin conexión». Sin conexión, el cierre queda
    // guardado y se envía en el próximo ingreso (si ingresa otra cuenta, el servidor lo rechaza y se descarta); mientras tanto,
    // el servidor la cierra sola a los 30 minutos (HU37).
    private async Task CerrarSesionAsync()
    {
        var enCurso = acompanamiento?.Estado is EstadoAcompanamiento.Activo or EstadoAcompanamiento.CierrePendiente or EstadoAcompanamiento.Finalizando;
        if (!await (enCurso ? ConfirmarCierreConAcompanamiento() : ConfirmarCierre()))
            return;
        if (acompanamiento?.Estado == EstadoAcompanamiento.Activo)
        {
            (TipoMensaje, Mensaje) = ("informacion", "Finalizando tu acompañamiento…");
            await acompanamiento.FinalizarAsync();
        }
        if (acompanamiento is not null)
            await acompanamiento.ReiniciarAsync();
        // Un SOS pendiente de reintento no puede reenviarse luego con la sesión de otra persona.
        if (emisor is not null)
            await emisor.ReiniciarAsync();
        sesiones.Cerrar();
        await navegacion.IrAAsync(Rutas.Ingreso);
    }
}

// ADR-08, fase D — «Mis datos», con el formulario y las reglas del registro único (solo la dirección es opcional). Las cuentas
// creadas antes del registro único llegan aquí para completar lo que falta.
public sealed class MisDatosVistaModelo : FormularioDatosCiudadano
{
    public const string AvisoIncompletos =
        "Completa tu documento, tu fecha de nacimiento y tu contacto de emergencia: la central los necesita para atenderte.";

    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private readonly TimeProvider reloj;
    private string celular = "", celularNumero = "";

    public MisDatosVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider? reloj = null)
    {
        (this.api, this.sesiones, this.navegacion) = (api, sesiones, navegacion);
        this.reloj = reloj ?? TimeProvider.System;
        Guardar = new ComandoAsincrono(GuardarAsync);
    }

    public string Celular { get => celular; private set => Asignar(ref celular, value); }

    public ComandoAsincrono Guardar { get; }

    public async Task CargarAsync()
    {
        if (await sesiones.LeerAsync() is not { } sesion)
            return;
        switch (await api.ConsultarPerfilAsync(sesion.Token, CancellationToken.None))
        {
            case ResultadoApi<PerfilCiudadanoVista>.Correcto { Valor: var p }:
                Cargar(p);
                celularNumero = p.Celular;
                Celular = p.Celular.Length == 9 ? $"+51 {FormatoCelular.Mostrar(p.Celular)}" : p.Celular;
                if (!p.DatosCompletos)
                    (TipoMensaje, Mensaje) = ("advertencia", AvisoIncompletos);
                break;
            case ResultadoApi<PerfilCiudadanoVista>.Rechazado { Estado: 401 }:
                sesiones.Cerrar();
                await navegacion.IrAAsync(Rutas.Ingreso);
                break;
            case ResultadoApi<PerfilCiudadanoVista>.SinRespuesta sinRespuesta:
                (TipoMensaje, Mensaje) = ("advertencia", sinRespuesta.Motivo);
                break;
        }
    }

    private async Task GuardarAsync()
    {
        Mensaje = "";
        if (Validar(Hoy(reloj), celularNumero.Length > 0 ? celularNumero : null) is not { } d)
            return;
        var solicitud = new SolicitudPerfilCiudadano(d.Nombres ?? "", d.Apellidos ?? "", d.TipoDocumento, d.NumeroDocumento, d.FechaNacimiento,
            d.Direccion, d.ContactoNombre, d.ContactoParentesco, d.ContactoTelefono);

        if (await sesiones.LeerAsync() is not { } sesion)
        {
            await navegacion.IrAAsync(Rutas.Ingreso);
            return;
        }
        switch (await api.ActualizarPerfilAsync(sesion.Token, solicitud, CancellationToken.None))
        {
            case ResultadoApi<PerfilCiudadanoVista>.Correcto:
                MostrarErrores(new Dictionary<string, string[]>());
                (TipoMensaje, Mensaje) = ("exito", "Tus datos se guardaron.");
                break;
            case ResultadoApi<PerfilCiudadanoVista>.Rechazado { Estado: 401 }:
                sesiones.Cerrar();
                await navegacion.IrAAsync(Rutas.Ingreso);
                break;
            case ResultadoApi<PerfilCiudadanoVista>.Rechazado rechazo:
                MostrarErrores(rechazo.Errores);
                (TipoMensaje, Mensaje) = ("error", rechazo.Errores.Count > 0 ? "Revisa los datos marcados." : rechazo.Mensaje);
                break;
            case ResultadoApi<PerfilCiudadanoVista>.SinRespuesta sinRespuesta:
                (TipoMensaje, Mensaje) = ("error", sinRespuesta.Motivo);
                break;
        }
    }
}
