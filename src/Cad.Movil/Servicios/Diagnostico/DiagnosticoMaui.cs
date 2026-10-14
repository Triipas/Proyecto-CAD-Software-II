namespace Cad.Movil.Servicios.Diagnostico;

// Implementaciones MAUI de la sonda de SPIKE01.

// La ventana informa cuándo la app pasa a segundo plano (Stopped) y vuelve (Resumed); App la conecta.
public sealed class EstadoAppMaui : IEstadoApp
{
    public PlanoApp Plano { get; private set; } = PlanoApp.Primero;

    public void Conectar(Window ventana)
    {
        ventana.Stopped += (_, _) => Plano = PlanoApp.Segundo;
        ventana.Resumed += (_, _) => Plano = PlanoApp.Primero;
    }
}

public sealed class EstadoRedMaui : IEstadoRed
{
    public string Describir()
    {
        var acceso = Connectivity.Current.NetworkAccess;
        var perfiles = string.Join("+", Connectivity.Current.ConnectionProfiles);
        return acceso == NetworkAccess.Internet
            ? $"Internet ({perfiles})"
            : $"{acceso}{(perfiles.Length > 0 ? $" ({perfiles})" : "")}";
    }
}

// Registro de las muestras en un CSV del almacenamiento privado de la app. Con un APK de depuración se
// recupera con: adb exec-out run-as pe.cadmvp.movil cat files/spike01.csv
public sealed class RegistroSondaArchivo : IRegistroSonda
{
    public string Ruta { get; } = Path.Combine(FileSystem.AppDataDirectory, "spike01.csv");

    public async Task AgregarAsync(MuestraSonda muestra)
    {
        if (!File.Exists(Ruta))
            await File.WriteAllTextAsync(Ruta, SondaUbicacion.CabeceraCsv + Environment.NewLine);
        await File.AppendAllTextAsync(Ruta, SondaUbicacion.ACsv(muestra) + Environment.NewLine);
    }

    public void Borrar() => File.Delete(Ruta);
}
