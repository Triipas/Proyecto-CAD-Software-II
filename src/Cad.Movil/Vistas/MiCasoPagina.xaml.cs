using Cad.Movil.Servicios;
using Cad.Movil.VistasModelo;

namespace Cad.Movil.Vistas;

// Se abre con «mi-caso?id=…» desde «Mis casos»; con «&calificar=1» (el caso acaba de cerrarse), con la evaluación a la vista.
public partial class MiCasoPagina : ContentPage, IQueryAttributable
{
    private readonly MiCasoVistaModelo vistaModelo;
    private Guid casoId;
    private bool ofrecerEvaluacion;
    private IDispatcherTimer? revision;

    public MiCasoPagina(MiCasoVistaModelo vistaModelo)
    {
        InitializeComponent();
        BindingContext = this.vistaModelo = vistaModelo;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var valor) && Guid.TryParse(valor?.ToString(), out var id))
            casoId = id;
        ofrecerEvaluacion = query.TryGetValue(Rutas.ParametroCalificar, out var calificar) && calificar?.ToString() == "1";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (casoId == Guid.Empty)
            return;
        // La invitación se hace una vez: al volver a esta pantalla (por ejemplo, desde otra) ya no se repite.
        var ofrecer = ofrecerEvaluacion;
        ofrecerEvaluacion = false;
        await vistaModelo.CargarAsync(casoId, ofrecer);
        IniciarRevision();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        revision?.Stop();
    }

    // Sin avisos push para el ciudadano: mientras el caso sigue abierto y la pantalla está a la vista, se consulta cada pocos segundos
    // para que, al cerrarse, la evaluación se ofrezca sin salir de aquí.
    private void IniciarRevision()
    {
        if (revision is null)
        {
            revision = Dispatcher.CreateTimer();
            revision.Interval = MiCasoVistaModelo.IntervaloRevision;
            revision.Tick += async (_, _) =>
            {
                if (vistaModelo.EnCurso)
                    await vistaModelo.RevisarAsync();
                else
                    revision.Stop();
            };
        }
        if (vistaModelo.EnCurso)
            revision.Start();
    }
}
