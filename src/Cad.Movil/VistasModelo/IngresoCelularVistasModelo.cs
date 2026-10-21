using Cad.Contratos.Cuentas;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// ADR-08 — ingreso del ciudadano con celular verificado y PIN: bienvenida → código → crea tu cuenta → PIN.

public enum PropositoCodigo { Acceder, RestablecerPin }

// Cambiar (ADR-08, fase D): desde «Perfil», con el PIN actual.
public enum ModoPin { Ingresar, Crear, Restablecer, Cambiar }

public static class FormatoCelular
{
    // Igual que el servidor: «+51 987 654 321» → «987654321».
    public static string Normalizar(string? texto)
    {
        var digitos = new string((texto ?? "").Where(char.IsAsciiDigit).ToArray());
        return digitos.Length == 11 && digitos.StartsWith("51") ? digitos[2..] : digitos;
    }

    public static bool EsValido(string celular) => celular.Length == 9 && celular[0] == '9';

    public static string Mostrar(string celular) =>
        celular.Length == 9 ? $"{celular[..3]} {celular[3..6]} {celular[6..]}" : celular;
}

public static class ReglasPin
{
    public const int Longitud = 6;

    // Las mismas reglas que el servidor, para avisar antes de enviar: sin repetidos (111111) ni secuencias (123456).
    public static string? Validar(string pin)
    {
        if (pin.Length != Longitud || !pin.All(char.IsAsciiDigit))
            return "El PIN debe tener exactamente 6 dígitos.";
        var pasos = pin.Zip(pin.Skip(1), (a, b) => b - a).Distinct().ToArray();
        return pin.Distinct().Count() == 1 || pasos is [1] or [-1]
            ? "Ese PIN es fácil de adivinar: evita números repetidos o seguidos."
            : null;
    }
}

// Estado del flujo entre pantallas (una sola instancia) y los pasos que comparten: enviar el código y abrir la sesión.
public sealed class FlujoIngresoCelular(IApiCad api, IAlmacenSesion sesiones, IAlmacenDispositivo dispositivos,
    INavegacion navegacion, TimeProvider reloj, EmisorSos? emisor = null)
{
    public string Celular { get; private set; } = "";
    public PropositoCodigo Proposito { get; set; }
    public ModoPin ModoPin { get; set; }
    public string? Llave { get; set; }
    public string? TokenVerificacion { get; set; }
    public bool CuentaExiste { get; set; }
    public bool PuedeRegistrarDeNuevo { get; set; }
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public DateTimeOffset ReenviarDesde { get; private set; }
    public string? CodigoDePrueba { get; private set; }
    // Mensaje que la pantalla siguiente debe mostrar al aparecer (p. ej., «este teléfono ya no está vinculado»).
    public string? AvisoPendiente { get; set; }

    public void Reiniciar(string celular)
    {
        Celular = celular;
        Proposito = PropositoCodigo.Acceder;
        ModoPin = ModoPin.Ingresar;
        Llave = TokenVerificacion = CodigoDePrueba = AvisoPendiente = null;
        CuentaExiste = PuedeRegistrarDeNuevo = false;
        Nombres = Apellidos = "";
        ReenviarDesde = DateTimeOffset.MinValue;
    }

    // Devuelve el mensaje de error, o null si el código salió.
    public async Task<string?> EnviarCodigoAsync()
    {
        TokenVerificacion = null;
        switch (await api.EnviarCodigoCelularAsync(new SolicitudCodigoCelular(Celular), CancellationToken.None))
        {
            case ResultadoApi<RespuestaCodigoCelular>.Correcto correcto:
                ReenviarDesde = reloj.GetUtcNow().AddSeconds(correcto.Valor.ReenviarEnSegundos);
                CodigoDePrueba = correcto.Valor.CodigoDePrueba;
                return null;
            case ResultadoApi<RespuestaCodigoCelular>.Rechazado rechazo:
                return rechazo.Errores.Values.FirstOrDefault()?.FirstOrDefault() ?? rechazo.Mensaje;
            case ResultadoApi<RespuestaCodigoCelular>.SinRespuesta sinRespuesta:
                return sinRespuesta.Motivo;
            default:
                return "No se pudo enviar el código.";
        }
    }

    // La cuenta quedó abierta en este teléfono: se guardan la sesión y, si es nueva, la llave del dispositivo.
    public async Task CompletarAsync(RespuestaIngresoCelular respuesta)
    {
        // Sesión nueva (quizá de otra cuenta): nada del SOS anterior se puede reintentar con ella.
        if (emisor is not null)
            await emisor.ReiniciarAsync();
        await sesiones.GuardarAsync(new SesionApp(respuesta.Token, respuesta.Rol, respuesta.ExpiraEn));
        if (respuesta.LlaveDispositivo is { } llave)
            await dispositivos.GuardarAsync(new DispositivoVinculado(Celular, llave));
        TokenVerificacion = null;
        Nombres = Apellidos = "";
        await navegacion.IrAAsync(Rutas.Ciudadano);
    }

    public async Task<string?> TokenAsync() => (await sesiones.LeerAsync())?.Token;

    // Cambio de PIN: la sesión se renueva sin cambiar de pantalla ni de teléfono vinculado.
    public Task GuardarSesionAsync(RespuestaIngresoCelular respuesta) =>
        sesiones.GuardarAsync(new SesionApp(respuesta.Token, respuesta.Rol, respuesta.ExpiraEn));

    public void OlvidarDispositivo()
    {
        dispositivos.Olvidar();
        Llave = null;
    }
}

// Paso 1: el número. Si este teléfono ya está vinculado a ese número, basta el PIN.
public sealed class BienvenidaVistaModelo : VistaModeloBase
{
    private readonly FlujoIngresoCelular flujo;
    private readonly IAlmacenDispositivo dispositivos;
    private readonly INavegacion navegacion;
    private string celular = "", mensaje = "";

    public BienvenidaVistaModelo(FlujoIngresoCelular flujo, IAlmacenDispositivo dispositivos, INavegacion navegacion)
    {
        (this.flujo, this.dispositivos, this.navegacion) = (flujo, dispositivos, navegacion);
        Continuar = new ComandoAsincrono(ContinuarAsync);
        IrAIngresoSereno = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.IngresoSereno));
    }

    public string Celular { get => celular; set => Asignar(ref celular, value); }
    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => Mensaje.Length > 0;

    public ComandoAsincrono Continuar { get; }
    public ComandoAsincrono IrAIngresoSereno { get; }

    // Al volver a la app tras cerrar sesión, el número del teléfono vinculado ya está escrito.
    public async Task PrepararAsync()
    {
        if (Celular.Length == 0 && await dispositivos.LeerAsync() is { } vinculado)
            Celular = FormatoCelular.Mostrar(vinculado.Celular);
    }

    private async Task ContinuarAsync()
    {
        Mensaje = "";
        var numero = FormatoCelular.Normalizar(Celular);
        if (!FormatoCelular.EsValido(numero))
        {
            Mensaje = "Escribe un celular peruano de 9 dígitos que empiece por 9.";
            return;
        }

        flujo.Reiniciar(numero);
        if (await dispositivos.LeerAsync() is { } vinculado && vinculado.Celular == numero)
        {
            flujo.Llave = vinculado.Llave;
            flujo.ModoPin = ModoPin.Ingresar;
            await navegacion.IrAAsync(Rutas.Pin);
            return;
        }

        if (await flujo.EnviarCodigoAsync() is { } error)
        {
            Mensaje = error;
            return;
        }
        await navegacion.IrAAsync(Rutas.Codigo);
    }
}

// Paso 2: el código de 6 dígitos. Se valida solo al escribir el sexto dígito.
public sealed class CodigoVistaModelo : VistaModeloBase
{
    private readonly FlujoIngresoCelular flujo;
    private readonly IApiCad api;
    private readonly INavegacion navegacion;
    private readonly TimeProvider reloj;
    private string codigo = "", mensaje = "", textoReenviar = "";
    private bool puedeReenviar;

    public CodigoVistaModelo(FlujoIngresoCelular flujo, IApiCad api, INavegacion navegacion, TimeProvider reloj)
    {
        (this.flujo, this.api, this.navegacion, this.reloj) = (flujo, api, navegacion, reloj);
        Verificar = new ComandoAsincrono(VerificarAsync);
        Reenviar = new ComandoAsincrono(ReenviarAsync, () => PuedeReenviar);
        CambiarNumero = new ComandoAsincrono(() => navegacion.IrAAsync(".."));
        mensaje = flujo.AvisoPendiente ?? "";
        flujo.AvisoPendiente = null;
        ActualizarCuentaAtras();
    }

    public string Destino => $"Te enviamos un código de 6 dígitos al {FormatoCelular.Mostrar(flujo.Celular)}.";

    public string Codigo
    {
        get => codigo;
        set
        {
            var limpio = new string((value ?? "").Where(char.IsAsciiDigit).Take(6).ToArray());
            if (Asignar(ref codigo, limpio) && limpio.Length == 6)
                _ = Verificar.EjecutarAsync();
        }
    }

    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => Mensaje.Length > 0;

    // Solo con el proveedor simulado y en la compilación Debug (pruebas en el emulador).
    public string CodigoDePrueba => $"Código de prueba (solo desarrollo): {flujo.CodigoDePrueba}";
    public bool HayCodigoDePrueba => Compilacion.EsDepuracion && flujo.CodigoDePrueba is not null;

    public string TextoReenviar { get => textoReenviar; private set => Asignar(ref textoReenviar, value); }
    public bool PuedeReenviar
    {
        get => puedeReenviar;
        private set { if (Asignar(ref puedeReenviar, value)) Reenviar.ActualizarDisponibilidad(); }
    }

    public ComandoAsincrono Verificar { get; }
    public ComandoAsincrono Reenviar { get; }
    public ComandoAsincrono CambiarNumero { get; }

    // La pantalla lo llama cada segundo.
    public void ActualizarCuentaAtras()
    {
        var faltan = flujo.ReenviarDesde - reloj.GetUtcNow();
        PuedeReenviar = faltan <= TimeSpan.Zero;
        TextoReenviar = PuedeReenviar ? "Reenviar código" : $"Reenviar en {(int)faltan.TotalMinutes}:{faltan.Seconds:00}";
    }

    private async Task ReenviarAsync()
    {
        Mensaje = "";
        Codigo = "";
        if (await flujo.EnviarCodigoAsync() is { } error)
            Mensaje = error;
        Notificar(nameof(CodigoDePrueba));
        Notificar(nameof(HayCodigoDePrueba));
        ActualizarCuentaAtras();
    }

    private async Task VerificarAsync()
    {
        if (Codigo.Length != 6) return;
        Mensaje = "";
        var resultado = await api.VerificarCelularAsync(new SolicitudVerificacionCelular(flujo.Celular, Codigo), CancellationToken.None);
        switch (resultado)
        {
            case ResultadoApi<RespuestaVerificacionCelular>.Correcto correcto:
                flujo.TokenVerificacion = correcto.Valor.TokenVerificacion;
                flujo.CuentaExiste = correcto.Valor.CuentaExiste;
                flujo.PuedeRegistrarDeNuevo = correcto.Valor.PuedeRegistrarDeNuevo;
                if (!correcto.Valor.CuentaExiste)
                {
                    await navegacion.IrAAsync($"../{Rutas.CrearCuenta}");
                }
                else
                {
                    flujo.ModoPin = flujo.Proposito == PropositoCodigo.RestablecerPin ? ModoPin.Restablecer : ModoPin.Ingresar;
                    await navegacion.IrAAsync($"../{Rutas.Pin}");
                }
                break;
            case ResultadoApi<RespuestaVerificacionCelular>.Rechazado rechazo:
                Mensaje = rechazo.Mensaje; // «Código incorrecto. Te quedan 4 intentos.» o «El código venció…»
                codigo = "";
                Notificar(nameof(Codigo));
                break;
            case ResultadoApi<RespuestaVerificacionCelular>.SinRespuesta sinRespuesta:
                Mensaje = sinRespuesta.Motivo;
                break;
        }
    }
}

// Paso 3 (número nuevo): solo nombres y apellidos; el celular ya está verificado.
public sealed class CrearCuentaVistaModelo : VistaModeloBase
{
    public const string TextoTerminos =
        "CAD Móvil es un MVP académico con datos simulados; no es un canal oficial de emergencias. " +
        "Usamos tu nombre, tu celular y la ubicación que compartes al pedir ayuda solo para atender tus casos: " +
        "la central y el sereno asignado los ven mientras te atienden. Tu número se envía a un proveedor de SMS solo para mandarte " +
        "el código de verificación. No usamos tus datos para publicidad.";

    private readonly FlujoIngresoCelular flujo;
    private readonly INavegacion navegacion;
    private string nombres = "", apellidos = "", errorNombres = "", errorApellidos = "", errorTerminos = "";
    private bool aceptaTerminos, mostrarTerminos;

    public CrearCuentaVistaModelo(FlujoIngresoCelular flujo, INavegacion navegacion)
    {
        (this.flujo, this.navegacion) = (flujo, navegacion);
        Continuar = new ComandoAsincrono(ContinuarAsync);
        AlternarTerminos = new ComandoAsincrono(() => { MostrarTerminos = !MostrarTerminos; return Task.CompletedTask; });
        nombres = flujo.Nombres;
        apellidos = flujo.Apellidos;
    }

    public string Celular => FormatoCelular.Mostrar(flujo.Celular);
    public string Nombres { get => nombres; set => Asignar(ref nombres, value); }
    public string Apellidos { get => apellidos; set => Asignar(ref apellidos, value); }
    public bool AceptaTerminos { get => aceptaTerminos; set { if (Asignar(ref aceptaTerminos, value) && value) ErrorTerminos = ""; } }
    public bool MostrarTerminos { get => mostrarTerminos; private set => Asignar(ref mostrarTerminos, value); }
    public string Terminos => TextoTerminos;

    public string ErrorNombres { get => errorNombres; private set { Asignar(ref errorNombres, value); Notificar(nameof(HayErrorNombres)); } }
    public string ErrorApellidos { get => errorApellidos; private set { Asignar(ref errorApellidos, value); Notificar(nameof(HayErrorApellidos)); } }
    public string ErrorTerminos { get => errorTerminos; private set { Asignar(ref errorTerminos, value); Notificar(nameof(HayErrorTerminos)); } }
    public bool HayErrorNombres => ErrorNombres.Length > 0;
    public bool HayErrorApellidos => ErrorApellidos.Length > 0;
    public bool HayErrorTerminos => ErrorTerminos.Length > 0;

    public ComandoAsincrono Continuar { get; }
    public ComandoAsincrono AlternarTerminos { get; }

    private async Task ContinuarAsync()
    {
        ErrorNombres = Nombres.Trim().Length == 0 ? "Escribe tus nombres." : Nombres.Trim().Length > 100 ? "Máximo 100 caracteres." : "";
        ErrorApellidos = Apellidos.Trim().Length == 0 ? "Escribe tus apellidos." : Apellidos.Trim().Length > 100 ? "Máximo 100 caracteres." : "";
        ErrorTerminos = AceptaTerminos ? "" : "Para crear tu cuenta debes aceptar los términos.";
        if (ErrorNombres.Length > 0 || ErrorApellidos.Length > 0 || ErrorTerminos.Length > 0)
            return;

        flujo.Nombres = Nombres.Trim();
        flujo.Apellidos = Apellidos.Trim();
        flujo.ModoPin = ModoPin.Crear;
        await navegacion.IrAAsync(Rutas.Pin);
    }
}

// Paso 4: el PIN con un teclado propio. Crear (y confirmar), restablecer o ingresar.
public sealed class PinVistaModelo : VistaModeloBase
{
    private readonly FlujoIngresoCelular flujo;
    private readonly IApiCad api;
    private readonly INavegacion navegacion;
    private string digitos = "", primero = "", mensaje = "";
    private string? actual; // ModoPin.Cambiar: el PIN actual ya escrito
    private bool confirmando, bloqueado;

    public PinVistaModelo(FlujoIngresoCelular flujo, IApiCad api, INavegacion navegacion)
    {
        (this.flujo, this.api, this.navegacion) = (flujo, api, navegacion);
        Tecla = new ComandoAsincrono<string>(PulsarAsync);
        Borrar = new ComandoAsincrono(() => { if (digitos.Length > 0) Digitos = digitos[..^1]; return Task.CompletedTask; });
        OlvideMiPin = new ComandoAsincrono(OlvideMiPinAsync);
        CambiarNumero = new ComandoAsincrono(() => navegacion.IrAAsync(Rutas.Ingreso));
        RegistrarDeNuevo = new ComandoAsincrono(() => navegacion.IrAAsync($"../{Rutas.CrearCuenta}"));
    }

    public ModoPin Modo => flujo.ModoPin;
    public bool EsIngreso => Modo == ModoPin.Ingresar;

    private bool PidiendoActual => Modo == ModoPin.Cambiar && actual is null;

    public string Titulo => (Modo, confirmando) switch
    {
        (ModoPin.Ingresar, _) => "Ingresa tu PIN",
        (ModoPin.Crear, false) => "Crea tu PIN de 6 dígitos",
        (ModoPin.Restablecer, false) => "Crea un PIN nuevo",
        (ModoPin.Cambiar, false) => PidiendoActual ? "Ingresa tu PIN actual" : "Crea tu PIN nuevo",
        (ModoPin.Cambiar, true) => "Confirma tu PIN nuevo",
        _ => "Confirma tu PIN",
    };

    public string Subtitulo => PidiendoActual ? "Para cambiarlo, primero confirma que eres tú."
        : Modo == ModoPin.Ingresar
        ? $"Hola de nuevo · {FormatoCelular.Mostrar(flujo.Celular)}"
        : confirmando ? "Escríbelo otra vez para confirmarlo." : "Lo usarás para volver a ingresar. Evita números repetidos o seguidos.";

    public string Digitos
    {
        get => digitos;
        private set
        {
            if (!Asignar(ref digitos, value)) return;
            Notificar(nameof(Puntos));
        }
    }

    // Seis indicadores: true = dígito escrito. El PIN nunca se muestra.
    public IReadOnlyList<bool> Puntos => Enumerable.Range(0, ReglasPin.Longitud).Select(i => i < digitos.Length).ToArray();

    public string Mensaje { get => mensaje; private set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => Mensaje.Length > 0;
    public bool Bloqueado { get => bloqueado; private set => Asignar(ref bloqueado, value); }
    public bool PuedeRegistrarDeNuevo => EsIngreso && flujo.PuedeRegistrarDeNuevo && flujo.TokenVerificacion is not null;

    public ComandoAsincrono<string> Tecla { get; }
    public ComandoAsincrono Borrar { get; }
    public ComandoAsincrono OlvideMiPin { get; }
    public ComandoAsincrono CambiarNumero { get; }
    public ComandoAsincrono RegistrarDeNuevo { get; }

    public void Preparar()
    {
        if (flujo.AvisoPendiente is { } aviso) { Mensaje = aviso; flujo.AvisoPendiente = null; }
    }

    private async Task PulsarAsync(string tecla)
    {
        if (Bloqueado || tecla.Length != 1 || !char.IsAsciiDigit(tecla[0]) || digitos.Length >= ReglasPin.Longitud)
            return;
        if (digitos.Length == 0 && !Bloqueado) Mensaje = "";
        Digitos = digitos + tecla;
        if (digitos.Length == ReglasPin.Longitud)
            await CompletarPinAsync();
    }

    private async Task CompletarPinAsync()
    {
        var pin = digitos;
        Digitos = "";

        if (Modo == ModoPin.Ingresar)
        {
            await IngresarAsync(pin);
            return;
        }

        if (PidiendoActual)
        {
            actual = pin;
            CambiarPaso(confirmar: false);
            return;
        }

        if (!confirmando)
        {
            if (ReglasPin.Validar(pin) is { } error) { Mensaje = error; return; }
            if (pin == actual) { Mensaje = "El PIN nuevo debe ser distinto del actual."; return; }
            primero = pin;
            CambiarPaso(confirmar: true);
            return;
        }

        if (pin != primero)
        {
            Mensaje = "Los PIN no coinciden. Vuelve a crearlo.";
            CambiarPaso(confirmar: false);
            return;
        }

        if (Modo == ModoPin.Cambiar)
        {
            await CambiarAsync(pin);
            return;
        }

        var resultado = Modo == ModoPin.Crear
            ? await api.RegistrarConCelularAsync(new SolicitudRegistroCelular(flujo.TokenVerificacion ?? "", flujo.Nombres, flujo.Apellidos, pin, true), CancellationToken.None)
            : await api.RestablecerPinAsync(new SolicitudRestablecerPin(flujo.TokenVerificacion ?? "", pin), CancellationToken.None);
        primero = "";
        await AtenderAsync(resultado, alRechazoDeDatos: () => CambiarPaso(confirmar: false));
    }

    private async Task IngresarAsync(string pin)
    {
        var conToken = flujo.TokenVerificacion is not null;
        var resultado = await api.IngresarConCelularAsync(new SolicitudIngresoCelular(flujo.Celular, pin,
            conToken ? null : flujo.Llave, flujo.TokenVerificacion), CancellationToken.None);
        await AtenderAsync(resultado, alRechazoDeDatos: () => { });
    }

    private async Task AtenderAsync(ResultadoApi<RespuestaIngresoCelular> resultado, Action alRechazoDeDatos)
    {
        switch (resultado)
        {
            case ResultadoApi<RespuestaIngresoCelular>.Correcto correcto:
                Mensaje = "";
                await flujo.CompletarAsync(correcto.Valor);
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado { Estado: 423 } rechazo:
                Mensaje = rechazo.Mensaje;
                Bloqueado = true;
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado { Estado: 428 } rechazo:
                // Otro teléfono se vinculó después: este necesita verificarse con un código.
                flujo.OlvidarDispositivo();
                flujo.Proposito = PropositoCodigo.Acceder;
                if (await flujo.EnviarCodigoAsync() is { } error) { Mensaje = error; return; }
                flujo.AvisoPendiente = rechazo.Mensaje;
                await navegacion.IrAAsync($"../{Rutas.Codigo}");
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado { Estado: 400 } rechazo:
                Mensaje = rechazo.Errores.Values.FirstOrDefault()?.FirstOrDefault() ?? rechazo.Mensaje;
                alRechazoDeDatos();
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado rechazo:
                Mensaje = rechazo.Mensaje;
                break;
            case ResultadoApi<RespuestaIngresoCelular>.SinRespuesta sinRespuesta:
                Mensaje = sinRespuesta.Motivo;
                break;
        }
    }

    // ADR-08, fase D: cambio de PIN desde «Perfil». El servidor emite una sesión nueva (cambió el sello de la cuenta).
    private async Task CambiarAsync(string pinNuevo)
    {
        var pinActual = actual ?? "";
        primero = "";
        if (await flujo.TokenAsync() is not { } token)
        {
            await navegacion.IrAAsync(Rutas.Ingreso);
            return;
        }
        switch (await api.CambiarPinAsync(token, new SolicitudCambioPin(pinActual, pinNuevo), CancellationToken.None))
        {
            case ResultadoApi<RespuestaIngresoCelular>.Correcto correcto:
                await flujo.GuardarSesionAsync(correcto.Valor);
                flujo.AvisoPendiente = "Cambiaste tu PIN.";
                await navegacion.IrAAsync("..");
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado { Estado: 400, Errores.Count: 0 } rechazo:
                // PIN actual incorrecto: se vuelve a pedir desde el principio.
                Mensaje = rechazo.Mensaje;
                actual = null;
                CambiarPaso(confirmar: false);
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado { Estado: 400 } rechazo:
                Mensaje = rechazo.Errores.Values.FirstOrDefault()?.FirstOrDefault() ?? rechazo.Mensaje;
                CambiarPaso(confirmar: false);
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado { Estado: 423 } rechazo:
                Mensaje = rechazo.Mensaje;
                Bloqueado = true;
                break;
            case ResultadoApi<RespuestaIngresoCelular>.Rechazado rechazo:
                Mensaje = rechazo.Mensaje;
                break;
            case ResultadoApi<RespuestaIngresoCelular>.SinRespuesta sinRespuesta:
                Mensaje = sinRespuesta.Motivo;
                break;
        }
    }

    private async Task OlvideMiPinAsync()
    {
        Mensaje = "";
        flujo.Proposito = PropositoCodigo.RestablecerPin;
        if (await flujo.EnviarCodigoAsync() is { } error) { Mensaje = error; return; }
        await navegacion.IrAAsync($"../{Rutas.Codigo}");
    }

    private void CambiarPaso(bool confirmar)
    {
        confirmando = confirmar;
        if (!confirmar) primero = "";
        Notificar(nameof(Titulo));
        Notificar(nameof(Subtitulo));
    }
}

// Comando asíncrono con parámetro (teclas del PIN).
public sealed class ComandoAsincrono<T>(Func<T, Task> accion) : System.Windows.Input.ICommand
{
    private bool ejecutando;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parametro) => !ejecutando;

    public async void Execute(object? parametro)
    {
        if (parametro is T valor) await EjecutarAsync(valor);
    }

    public async Task EjecutarAsync(T valor)
    {
        if (ejecutando) return;
        ejecutando = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await accion(valor); }
        finally
        {
            ejecutando = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
