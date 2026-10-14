using Cad.Movil.Servicios.Diagnostico;

namespace Cad.Movil;

public partial class App : Application
{
    private readonly AppShell shell;
    private readonly EstadoAppMaui estadoApp;

    public App(AppShell shell, EstadoAppMaui estadoApp)
    {
        InitializeComponent();
        (this.shell, this.estadoApp) = (shell, estadoApp);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var ventana = new Window(shell);
        estadoApp.Conectar(ventana); // primer o segundo plano (sonda de SPIKE01)
        return ventana;
    }
}
