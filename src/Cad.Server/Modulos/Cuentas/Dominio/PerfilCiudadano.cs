namespace Cad.Server.Modulos.Cuentas.Dominio;

// ADR-08, fase D: datos opcionales del ciudadano, separados de la cuenta. Solo los ve la central en el detalle de un caso
// o acompañamiento del propio ciudadano; nunca se escriben en los registros.
public sealed class PerfilCiudadano
{
    public Guid UsuarioId { get; private set; }
    public string? TipoDocumento { get; private set; }
    public string? NumeroDocumento { get; private set; }
    public DateOnly? FechaNacimiento { get; private set; }
    public string? Direccion { get; private set; }
    public string? ContactoNombre { get; private set; }
    public string? ContactoParentesco { get; private set; }
    public string? ContactoTelefono { get; private set; }
    public DateTimeOffset ActualizadoEn { get; private set; }

    private PerfilCiudadano() { } // EF Core

    public static PerfilCiudadano Nuevo(Guid usuarioId) => new() { UsuarioId = usuarioId };

    public void Actualizar(string? tipoDocumento, string? numeroDocumento, DateOnly? fechaNacimiento, string? direccion,
        string? contactoNombre, string? contactoParentesco, string? contactoTelefono, DateTimeOffset ahora)
    {
        (TipoDocumento, NumeroDocumento, FechaNacimiento, Direccion) = (tipoDocumento, numeroDocumento, fechaNacimiento, direccion);
        (ContactoNombre, ContactoParentesco, ContactoTelefono) = (contactoNombre, contactoParentesco, contactoTelefono);
        ActualizadoEn = ahora;
    }

    // DP25 (solo desarrollo): al liberar un número, el documento queda libre para la cuenta nueva.
    public void LiberarDocumento() => (TipoDocumento, NumeroDocumento) = (null, null);
}
