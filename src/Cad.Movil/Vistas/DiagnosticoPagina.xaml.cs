using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

public partial class DiagnosticoPagina : ContentPage
{
    public DiagnosticoPagina(DiagnosticoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = vistaModelo;
    }
}
