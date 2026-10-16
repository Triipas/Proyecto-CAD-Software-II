namespace Cad.Contratos.Casos;

// HU31 — bandeja de notificaciones del ciudadano (E4: dentro de la app). El texto es una vista previa sin detalles del
// hecho (CA03); el caso vinculado (CA01) se abre en «Mis casos».
public sealed record NotificacionCiudadanoVista(Guid Id, Guid CasoId, string Texto, DateTimeOffset CreadaEn, bool Leida);

public sealed record BandejaNotificaciones(int NoLeidas, IReadOnlyList<NotificacionCiudadanoVista> Notificaciones);
