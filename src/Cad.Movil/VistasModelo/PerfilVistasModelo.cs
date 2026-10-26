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

    public string TextoDatos => perfil is null ? "" : perfil.DatosCompletados == PerfilCiudadanoVista.DatosOpcionales
        ? "Datos completos"
        : $"Completa tus datos ({perfil.DatosCompletados} de {PerfilCiudadanoVista.DatosOpcionales})";
    public bool DatosIncompletos => perfil is not null && perfil.DatosCompletados < PerfilCiudadanoVista.DatosOpcionales;

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

// ADR-08, fase D — «Mis datos»: nombres y apellidos obligatorios; documento, nacimiento, dirección y contacto opcionales.
public sealed class MisDatosVistaModelo : VistaModeloBase
{
    public const string SinDocumento = "Sin documento";
    public const string SinContacto = "Elegir";

    private readonly IApiCad api;
    private readonly IAlmacenSesion sesiones;
    private readonly INavegacion navegacion;
    private string nombres = "", apellidos = "", celular = "", numeroDocumento = "", fechaNacimiento = "", direccion = "",
        contactoNombre = "", contactoTelefono = "", mensaje = "", tipoMensaje = "error";
    private string tipoDocumento = SinDocumento, contactoParentesco = SinContacto;
    private IReadOnlyDictionary<string, string[]> errores = new Dictionary<string, string[]>();

    public MisDatosVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion)
    {
        (this.api, this.sesiones, this.navegacion) = (api, sesiones, navegacion);
        Guardar = new ComandoAsincrono(GuardarAsync);
    }

    public IReadOnlyList<string> TiposDocumento { get; } =
        [SinDocumento, .. DatosCiudadano.TiposDocumento.Select(DatosCiudadano.NombreTipo)];
    public IReadOnlyList<string> Parentescos { get; } = [SinContacto, .. DatosCiudadano.Parentescos];

    public string Nombres { get => nombres; set => Asignar(ref nombres, value); }
    public string Apellidos { get => apellidos; set => Asignar(ref apellidos, value); }
    public string Celular { get => celular; private set => Asignar(ref celular, value); }
    public string TipoDocumento
    {
        get => tipoDocumento;
        set { if (Asignar(ref tipoDocumento, value ?? SinDocumento)) Notificar(nameof(ConDocumento)); }
    }
    public bool ConDocumento => TipoDocumento != SinDocumento;
    public string NumeroDocumento { get => numeroDocumento; set => Asignar(ref numeroDocumento, value); }
    public string FechaNacimiento { get => fechaNacimiento; set => Asignar(ref fechaNacimiento, value); }
    public string Direccion { get => direccion; set => Asignar(ref direccion, value); }
    public string ContactoNombre { get => contactoNombre; set => Asignar(ref contactoNombre, value); }
    public string ContactoParentesco { get => contactoParentesco; set => Asignar(ref contactoParentesco, value ?? SinContacto); }
    public string ContactoTelefono { get => contactoTelefono; set => Asignar(ref contactoTelefono, value); }

    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public string TipoMensaje { get => tipoMensaje; private set => Asignar(ref tipoMensaje, value); }
    public bool HayMensaje => Mensaje.Length > 0;

    public string ErrorNombres => Error("nombres");
    public string ErrorApellidos => Error("apellidos");
    public string ErrorDocumento => Error("tipoDocumento") is { Length: > 0 } e ? e : Error("numeroDocumento");
    public string ErrorFechaNacimiento => Error("fechaNacimiento");
    public string ErrorDireccion => Error("direccion");
    public string ErrorContacto => new[] { "contactoNombre", "contactoParentesco", "contactoTelefono" }.Select(Error).FirstOrDefault(e => e.Length > 0) ?? "";
    public bool HayErrorNombres => ErrorNombres.Length > 0;
    public bool HayErrorApellidos => ErrorApellidos.Length > 0;
    public bool HayErrorDocumento => ErrorDocumento.Length > 0;
    public bool HayErrorFechaNacimiento => ErrorFechaNacimiento.Length > 0;
    public bool HayErrorDireccion => ErrorDireccion.Length > 0;
    public bool HayErrorContacto => ErrorContacto.Length > 0;

    public ComandoAsincrono Guardar { get; }

    public async Task CargarAsync()
    {
        if (await sesiones.LeerAsync() is not { } sesion)
            return;
        switch (await api.ConsultarPerfilAsync(sesion.Token, CancellationToken.None))
        {
            case ResultadoApi<PerfilCiudadanoVista>.Correcto { Valor: var p }:
                Nombres = p.Nombres;
                Apellidos = p.Apellidos;
                Celular = p.Celular.Length == 9 ? $"+51 {FormatoCelular.Mostrar(p.Celular)}" : p.Celular;
                TipoDocumento = p.TipoDocumento is { } t ? DatosCiudadano.NombreTipo(t) : SinDocumento;
                NumeroDocumento = p.NumeroDocumento ?? "";
                FechaNacimiento = p.FechaNacimiento?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
                Direccion = p.Direccion ?? "";
                ContactoNombre = p.ContactoNombre ?? "";
                ContactoParentesco = p.ContactoParentesco ?? SinContacto;
                ContactoTelefono = p.ContactoTelefono ?? "";
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
        DateOnly? nacimiento = null;
        if (FechaNacimiento.Trim().Length > 0)
        {
            if (!DateOnly.TryParseExact(FechaNacimiento.Trim(), ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            {
                MostrarErrores(new Dictionary<string, string[]> { ["fechaNacimiento"] = ["Escribe la fecha como día/mes/año, por ejemplo 17/05/1990."] });
                (TipoMensaje, Mensaje) = ("error", "Revisa los datos marcados.");
                return;
            }
            nacimiento = fecha;
        }

        var tipo = DatosCiudadano.TiposDocumento.FirstOrDefault(t => DatosCiudadano.NombreTipo(t) == TipoDocumento);
        var solicitud = new SolicitudPerfilCiudadano(Nombres, Apellidos, tipo, tipo is null ? null : Vacio(NumeroDocumento), nacimiento,
            Vacio(Direccion), Vacio(ContactoNombre), ContactoParentesco == SinContacto ? null : ContactoParentesco, Vacio(ContactoTelefono));

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

    private static string? Vacio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private string Error(string campo) => errores.TryGetValue(campo, out var m) && m.Length > 0 ? m[0] : "";

    private void MostrarErrores(IReadOnlyDictionary<string, string[]> nuevos)
    {
        errores = nuevos;
        foreach (var p in new[] { nameof(ErrorNombres), nameof(ErrorApellidos), nameof(ErrorDocumento), nameof(ErrorFechaNacimiento),
                     nameof(ErrorDireccion), nameof(ErrorContacto) })
        {
            Notificar(p);
            Notificar("Hay" + p);
        }
    }
}
