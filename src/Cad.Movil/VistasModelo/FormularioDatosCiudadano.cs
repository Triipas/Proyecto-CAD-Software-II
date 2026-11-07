using System.Globalization;
using Cad.Contratos.Cuentas;

namespace Cad.Movil.VistasModelo;

// Registro único (06/11/2026): los datos del ciudadano se piden con el mismo formulario al crear la cuenta y en «Mis datos».
// La regla es la de Cad.Contratos (ValidacionDatosCiudadano), la misma que vuelve a aplicar el servidor.
public abstract class FormularioDatosCiudadano : VistaModeloBase
{
    public const string SinElegir = "Elegir";

    private string nombres = "", apellidos = "", numeroDocumento = "", fechaNacimiento = "", direccion = "",
        contactoNombre = "", contactoTelefono = "", mensaje = "", tipoMensaje = "error";
    private string tipoDocumento = SinElegir, contactoParentesco = SinElegir;
    private IReadOnlyDictionary<string, string[]> errores = new Dictionary<string, string[]>();

    public IReadOnlyList<string> TiposDocumento { get; } =
        [SinElegir, .. DatosCiudadano.TiposDocumento.Select(DatosCiudadano.NombreTipo)];
    public IReadOnlyList<string> Parentescos { get; } = [SinElegir, .. DatosCiudadano.Parentescos];

    public string Nombres { get => nombres; set => Asignar(ref nombres, value); }
    public string Apellidos { get => apellidos; set => Asignar(ref apellidos, value); }
    public string TipoDocumento { get => tipoDocumento; set => Asignar(ref tipoDocumento, value ?? SinElegir); }
    public string NumeroDocumento { get => numeroDocumento; set => Asignar(ref numeroDocumento, value); }
    public string FechaNacimiento { get => fechaNacimiento; set => Asignar(ref fechaNacimiento, value); }
    public string Direccion { get => direccion; set => Asignar(ref direccion, value); }
    public string ContactoNombre { get => contactoNombre; set => Asignar(ref contactoNombre, value); }
    public string ContactoParentesco { get => contactoParentesco; set => Asignar(ref contactoParentesco, value ?? SinElegir); }
    public string ContactoTelefono { get => contactoTelefono; set => Asignar(ref contactoTelefono, value); }

    public string Mensaje { get => mensaje; protected set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public string TipoMensaje { get => tipoMensaje; protected set => Asignar(ref tipoMensaje, value); }
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

    // Campos que pertenecen a este formulario: los errores del servidor sobre ellos se muestran aquí.
    public static readonly IReadOnlySet<string> Campos = new HashSet<string>
    {
        "nombres", "apellidos", "tipoDocumento", "numeroDocumento", "fechaNacimiento", "direccion",
        "contactoNombre", "contactoParentesco", "contactoTelefono",
    };

    protected void Cargar(PerfilCiudadanoVista p)
    {
        Nombres = p.Nombres;
        Apellidos = p.Apellidos;
        TipoDocumento = p.TipoDocumento is { } t ? DatosCiudadano.NombreTipo(t) : SinElegir;
        NumeroDocumento = p.NumeroDocumento ?? "";
        FechaNacimiento = p.FechaNacimiento?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        Direccion = p.Direccion ?? "";
        ContactoNombre = p.ContactoNombre ?? "";
        ContactoParentesco = p.ContactoParentesco ?? SinElegir;
        ContactoTelefono = p.ContactoTelefono ?? "";
    }

    protected void Cargar(DatosCiudadanoEscritos d)
    {
        Nombres = d.Nombres ?? "";
        Apellidos = d.Apellidos ?? "";
        TipoDocumento = d.TipoDocumento is { } t ? DatosCiudadano.NombreTipo(t) : SinElegir;
        NumeroDocumento = d.NumeroDocumento ?? "";
        FechaNacimiento = d.FechaNacimiento?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        Direccion = d.Direccion ?? "";
        ContactoNombre = d.ContactoNombre ?? "";
        ContactoParentesco = d.ContactoParentesco ?? SinElegir;
        ContactoTelefono = d.ContactoTelefono ?? "";
    }

    // Valida en el teléfono con la regla compartida. Devuelve los datos tal como se envían, o null y los errores a la vista.
    protected DatosCiudadanoEscritos? Validar(DateOnly hoy, string? celularPropio, Dictionary<string, string[]>? erroresPropios = null)
    {
        var encontrados = erroresPropios ?? new Dictionary<string, string[]>();
        DateOnly? nacimiento = null;
        var fechaMalEscrita = false;
        if (FechaNacimiento.Trim().Length > 0)
        {
            if (LeerFecha(FechaNacimiento) is { } fecha)
                nacimiento = fecha;
            else
                fechaMalEscrita = true;
        }

        var tipo = DatosCiudadano.TiposDocumento.FirstOrDefault(t => DatosCiudadano.NombreTipo(t) == TipoDocumento);
        var datos = new DatosCiudadanoEscritos(Vacio(Nombres), Vacio(Apellidos), tipo, Vacio(NumeroDocumento), nacimiento,
            Vacio(Direccion), Vacio(ContactoNombre), ContactoParentesco == SinElegir ? null : ContactoParentesco, Vacio(ContactoTelefono));

        ValidacionDatosCiudadano.Validar(encontrados, datos, hoy, celularPropio);
        if (fechaMalEscrita)
            encontrados["fechaNacimiento"] = ["Escribe la fecha como día, mes y año, por ejemplo 17/05/1990 o 17051990."];
        MostrarErrores(encontrados);
        if (encontrados.Count == 0)
            return datos;
        (TipoMensaje, Mensaje) = ("error", "Revisa los datos marcados.");
        return null;
    }

    // El teclado numérico de Android no trae «/»: se acepta la fecha solo con dígitos (17051990) o con cualquier separador
    // (17/05/1990, 17-05-1990, 17.05.1990, 17 05 1990).
    public static DateOnly? LeerFecha(string texto)
    {
        var partes = System.Text.RegularExpressions.Regex.Split(texto.Trim(), @"\D+").Where(p => p.Length > 0).ToArray();
        var normalizada = partes switch
        {
            [var solo] when solo.Length == 8 => $"{solo[..2]}/{solo[2..4]}/{solo[4..]}",
            [var d, var m, var a] => $"{d}/{m}/{a}",
            _ => null,
        };
        return normalizada is not null && DateOnly.TryParseExact(normalizada, ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var fecha) ? fecha : null;
    }

    protected static DateOnly Hoy(TimeProvider reloj) => DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);

    protected static string? Vacio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private string Error(string campo) => errores.TryGetValue(campo, out var m) && m.Length > 0 ? m[0] : "";

    protected virtual void MostrarErrores(IReadOnlyDictionary<string, string[]> nuevos)
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
