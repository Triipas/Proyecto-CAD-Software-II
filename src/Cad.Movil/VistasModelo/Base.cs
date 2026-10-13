using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Cad.Movil.VistasModelo;

// MVVM con los enlaces de datos de MAUI (Plan de Arquitectura, Tabla 9), sin librerías adicionales.
public abstract class VistaModeloBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Asignar<T>(ref T campo, T valor, [CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
        campo = valor;
        Notificar(propiedad);
        return true;
    }

    protected void Notificar([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}

// Comando asíncrono: mientras se ejecuta queda deshabilitado, de modo que un doble toque no lo repite.
public sealed class ComandoAsincrono(Func<Task> accion, Func<bool>? puedeEjecutar = null) : ICommand
{
    private bool ejecutando;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parametro) => !ejecutando && (puedeEjecutar?.Invoke() ?? true);

    public async void Execute(object? parametro) => await EjecutarAsync();

    public async Task EjecutarAsync()
    {
        if (!CanExecute(null)) return;
        ejecutando = true;
        ActualizarDisponibilidad();
        try { await accion(); }
        finally
        {
            ejecutando = false;
            ActualizarDisponibilidad();
        }
    }

    public void ActualizarDisponibilidad() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
