using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

// Se abre con «mi-caso?id=…» desde «Mis casos».
public partial class MiCasoPagina : ContentPage, IQueryAttributable
{
    private readonly MiCasoVistaModelo vistaModelo;
    private Guid casoId;

    public MiCasoPagina(MiCasoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var valor) && Guid.TryParse(valor?.ToString(), out var id))
            casoId = id;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (casoId != Guid.Empty)
            await vistaModelo.CargarAsync(casoId);
    }
}
