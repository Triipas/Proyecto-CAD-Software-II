using Cad.Contratos.Sos;

namespace Cad.Movil.Servicios;

// Implementaciones con las API de .NET MAUI (Android). La lógica que las usa se prueba con dobles.

public sealed class ServicioUbicacionMaui : IServicioUbicacion
{
    private const string YaSolicitado = "cad.ubicacion.solicitada";

    // Android informa «Denied» también cuando el permiso nunca se ha pedido. Solo es una denegación
    // definitiva (orientar a los ajustes, HU06-CA03) si ya se pidió y el sistema no volverá a mostrar
    // el diálogo; en otro caso se explica primero y luego se solicita (HU06-CA01).
    public async Task<EstadoPermisoUbicacion> ConsultarPermisoAsync()
    {
        var estado = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (estado == PermissionStatus.Denied &&
            (!Preferences.Default.Get(YaSolicitado, false) ||
             await MainThread.InvokeOnMainThreadAsync(Permissions.ShouldShowRationale<Permissions.LocationWhenInUse>)))
            return EstadoPermisoUbicacion.Desconocido;
        return Convertir(estado);
    }

    public async Task<EstadoPermisoUbicacion> SolicitarPermisoAsync()
    {
        Preferences.Default.Set(YaSolicitado, true);
        return Convertir(await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.LocationWhenInUse>));
    }

    // Siempre una lectura nueva del sistema: nunca GetLastKnownLocationAsync (HU08-CA03).
    public async Task<UbicacionSos?> ObtenerActualAsync(TimeSpan tiempoMaximo, CancellationToken ct)
    {
        var lectura = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.High, tiempoMaximo), ct);
        return lectura is null
            ? null
            : new UbicacionSos(lectura.Latitude, lectura.Longitude, lectura.Accuracy ?? 0, lectura.Timestamp);
    }

    private static EstadoPermisoUbicacion Convertir(PermissionStatus estado) => estado switch
    {
        PermissionStatus.Granted => EstadoPermisoUbicacion.Concedido,
        PermissionStatus.Unknown => EstadoPermisoUbicacion.Desconocido,
        PermissionStatus.Restricted or PermissionStatus.Limited => EstadoPermisoUbicacion.Restringido,
        _ => EstadoPermisoUbicacion.Denegado,
    };
}

// JWT en SecureStorage (Android Keystore), nunca en preferencias en texto plano (Plan de Arquitectura, Tabla 21).
public sealed class AlmacenSesionSeguro : IAlmacenSesion
{
    private const string Clave = "cad.sesion";

    public Task GuardarAsync(SesionApp sesion) =>
        SecureStorage.Default.SetAsync(Clave, System.Text.Json.JsonSerializer.Serialize(sesion));

    public async Task<SesionApp?> LeerAsync()
    {
        var valor = await SecureStorage.Default.GetAsync(Clave);
        return valor is null ? null : System.Text.Json.JsonSerializer.Deserialize<SesionApp>(valor);
    }

    public void Cerrar() => SecureStorage.Default.Remove(Clave);
}

public sealed class ConfiguracionServidorMaui : IConfiguracionServidor
{
    private const string Clave = "cad.servidor";

#if DEBUG
    // Emulador de Android → servidor local del equipo de desarrollo (10.0.2.2 es el anfitrión).
    private const string PorDefecto = "http://10.0.2.2:5220/";
#else
    private const string PorDefecto = "";
#endif

    public Uri? Direccion => Uri.TryCreate(Preferences.Default.Get(Clave, PorDefecto), UriKind.Absolute, out var uri) ? uri : null;

    public string? Guardar(string direccion)
    {
        var error = ValidacionDireccion.Validar(direccion, out var uri);
        if (error is null)
            Preferences.Default.Set(Clave, uri!.ToString());
        return error;
    }
}

public static class ValidacionDireccion
{
    // RNF09: HTTPS obligatorio, salvo el servidor local visto desde el emulador (solo para desarrollo).
    public static string? Validar(string direccion, out Uri? uri)
    {
        uri = null;
        var texto = direccion.Trim();
        if (!texto.EndsWith('/')) texto += "/";
        if (!Uri.TryCreate(texto, UriKind.Absolute, out var leida))
            return "La dirección del servidor no es válida.";
        var local = leida.Host is "10.0.2.2" or "localhost";
        if (leida.Scheme != Uri.UriSchemeHttps && !(local && leida.Scheme == Uri.UriSchemeHttp))
            return "La dirección debe empezar por https:// (canal cifrado).";
        uri = leida;
        return null;
    }
}

public interface INavegacion
{
    Task IrAAsync(string ruta);
}

public sealed class NavegacionShell : INavegacion
{
    public Task IrAAsync(string ruta) => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(ruta));
}
