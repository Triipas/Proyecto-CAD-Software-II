using System.Globalization;
using Cad.Contratos.Casos;
using Cad.Movil.Servicios;

namespace Cad.Movil.VistasModelo;

// Un caso propio en la lista «Mis casos».
public sealed class ElementoMiCaso(MiCasoResumen caso, string fecha, ComandoAsincrono abrir)
{
    public Guid Id => caso.Id;
    public string Titulo => $"{caso.Origen} · {caso.Estado}";
    public string Fecha => fecha;
    public string? Resultado => caso.Resultado;
    public bool HayResultado => !string.IsNullOrEmpty(caso.Resultado);
    public ComandoAsincrono Abrir { get; } = abrir;
    public string Descripcion => $"{caso.Origen}, estado {caso.Estado}, registrado el {fecha}";
}

// Un avance público del caso (DP05), sin responsables.
public sealed record ElementoAvance(string Hora, string Estado, string? Detalle)
{
    public bool HayDetalle => !string.IsNullOrEmpty(Detalle);
}

// Base común: consulta con la sesión guardada y traduce los resultados de la API a mensajes.
public abstract class ConsultaCiudadanoVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona)
    : VistaModeloBase
{
    protected IApiCad Api { get; } = api;
    protected TimeZoneInfo Zona { get; } = zona ?? TimeZoneInfo.Local;
    private string mensaje = "";
    private bool cargando, sesionTerminada;

    public string Mensaje { get => mensaje; protected set { Asignar(ref mensaje, value); Notificar(nameof(HayMensaje)); } }
    public bool HayMensaje => mensaje.Length > 0;
    public bool Cargando { get => cargando; protected set => Asignar(ref cargando, value); }
    public bool SesionTerminada { get => sesionTerminada; private set => Asignar(ref sesionTerminada, value); }

    public ComandoAsincrono VolverAIngresar { get; } = new(async () => { sesiones.Cerrar(); await navegacion.IrAAsync(Rutas.Ingreso); });

    protected string Local(DateTimeOffset momento, string formato = "dd/MM/yyyy HH:mm") =>
        TimeZoneInfo.ConvertTime(momento, Zona).ToString(formato, CultureInfo.InvariantCulture);

    // null si no hay sesión vigente (y se avisa).
    protected async Task<string?> TokenAsync()
    {
        var sesion = await sesiones.LeerAsync();
        if (sesion is not null && sesion.Vigente(reloj.GetUtcNow()))
            return sesion.Token;
        SesionTerminada = true;
        Mensaje = "Tu sesión terminó. Vuelve a ingresar.";
        return null;
    }

    protected void SinExito<T>(ResultadoApi<T> resultado, string noEncontrado)
    {
        switch (resultado)
        {
            case ResultadoApi<T>.Rechazado { Estado: 401 }:
                SesionTerminada = true;
                Mensaje = "Tu sesión terminó. Vuelve a ingresar.";
                break;
            case ResultadoApi<T>.Rechazado { Estado: 404 }:
                Mensaje = noEncontrado;
                break;
            case ResultadoApi<T>.Rechazado:
                Mensaje = "No se pudo consultar. Inténtalo de nuevo.";
                break;
            default:
                Mensaje = "No hay respuesta del servidor. Desliza hacia abajo para reintentar.";
                break;
        }
    }
}

// HU30 — «Mis casos»: cada SOS y reporte propio con origen, fecha y estado público (CA01). Se vuelve a consultar al
// abrir la pantalla y al deslizar hacia abajo, para ver el estado actual (CA02).
public sealed class MisCasosVistaModelo : ConsultaCiudadanoVistaModelo
{
    private readonly INavegacion navegacion;
    private IReadOnlyList<ElementoMiCaso> casos = [];
    private bool consultado;

    public MisCasosVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona = null)
        : base(api, sesiones, navegacion, reloj, zona)
    {
        this.navegacion = navegacion;
        Actualizar = new ComandoAsincrono(ActualizarAsync);
    }

    public ComandoAsincrono Actualizar { get; }
    public IReadOnlyList<ElementoMiCaso> Casos { get => casos; private set { Asignar(ref casos, value); Notificar(nameof(SinCasos)); } }
    public bool SinCasos => consultado && casos.Count == 0 && !HayMensaje;

    public Task AlAparecerAsync() => ActualizarAsync();

    private async Task ActualizarAsync()
    {
        Cargando = true;
        try
        {
            if (await TokenAsync() is not { } token)
                return;
            var resultado = await Api.ConsultarMisCasosAsync(token, CancellationToken.None);
            if (resultado is ResultadoApi<List<MiCasoResumen>>.Correcto correcto)
            {
                Mensaje = "";
                consultado = true;
                Casos = correcto.Valor.Select(c => new ElementoMiCaso(c, Local(c.RegistradoEn),
                    new ComandoAsincrono(() => navegacion.IrAAsync($"{Rutas.MiCaso}?id={c.Id}")))).ToList();
            }
            else
                SinExito(resultado, "No se encontraron tus casos.");
            Notificar(nameof(SinCasos));
        }
        finally
        {
            Cargando = false;
        }
    }
}

// HU30 — detalle de un caso propio: estado actual, avances públicos y, si terminó, el resultado público (CA01–CA03).
public sealed class MiCasoVistaModelo : ConsultaCiudadanoVistaModelo
{
    private Guid casoId;
    private MiCasoDetalle? caso;

    public MiCasoVistaModelo(IApiCad api, IAlmacenSesion sesiones, INavegacion navegacion, TimeProvider reloj, TimeZoneInfo? zona = null)
        : base(api, sesiones, navegacion, reloj, zona) =>
        Actualizar = new ComandoAsincrono(ActualizarAsync);

    public ComandoAsincrono Actualizar { get; }
    public bool HayCaso => caso is not null;
    public string Titulo => caso is { } c ? $"{c.Origen} · {c.Estado}" : "";
    public string Registrado => caso is { } c ? $"Registrado el {Local(c.RegistradoEn)}" : "";
    public string Identificador => caso is { } c ? $"Identificador: {c.Id}" : "";
    public string? Resultado => caso?.Resultado;
    public bool HayResultado => !string.IsNullOrEmpty(caso?.Resultado);
    public IReadOnlyList<ElementoAvance> Avances =>
        caso?.Avances.Select(a => new ElementoAvance(Local(a.En), a.Estado, a.Detalle)).ToList() ?? [];

    public Task CargarAsync(Guid id)
    {
        casoId = id;
        return ActualizarAsync();
    }

    private async Task ActualizarAsync()
    {
        Cargando = true;
        try
        {
            if (await TokenAsync() is not { } token)
                return;
            var resultado = await Api.ConsultarMiCasoAsync(token, casoId, CancellationToken.None);
            if (resultado is ResultadoApi<MiCasoDetalle>.Correcto correcto)
            {
                Mensaje = "";
                caso = correcto.Valor;
            }
            else
            {
                // CA04: un caso ajeno o inexistente no se muestra.
                if (resultado is ResultadoApi<MiCasoDetalle>.Rechazado { Estado: 404 })
                    caso = null;
                SinExito(resultado, "No se encontró el caso.");
            }
            foreach (var p in new[] { nameof(HayCaso), nameof(Titulo), nameof(Registrado), nameof(Identificador), nameof(Resultado),
                         nameof(HayResultado), nameof(Avances) })
                Notificar(p);
        }
        finally
        {
            Cargando = false;
        }
    }
}
