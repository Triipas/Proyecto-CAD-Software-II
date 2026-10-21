using Cad.Contratos.Cuentas;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Consultas;

// ADR-08, fase D — datos del ciudadano que el operador necesita para atender su caso o su acompañamiento: el celular
// (verificado o no) y los datos opcionales que haya completado. Solo se muestran en el detalle de un caso o una sesión,
// nunca en listas ni búsquedas, y nunca se escriben en los registros.
public sealed record FichaCiudadano(
    string Nombre,
    string? Telefono,
    bool TelefonoVerificado,
    DateOnly? FechaNacimiento,
    int? Edad,
    string? Documento,
    string? Direccion,
    string? ContactoNombre,
    string? ContactoParentesco,
    string? ContactoTelefono)
{
    public bool TieneContacto => ContactoTelefono is not null;
}

public sealed class ConsultarFichaCiudadano(CadDbContext db, TimeProvider reloj, IConfiguration configuracion)
{
    public async Task<FichaCiudadano?> PorCasoAsync(Guid casoId, CancellationToken ct) =>
        await db.Casos.AsNoTracking().Where(c => c.Id == casoId).Select(c => (Guid?)c.CiudadanoId).SingleOrDefaultAsync(ct) is { } id
            ? await PorCiudadanoAsync(id, ct)
            : null;

    public async Task<FichaCiudadano?> PorSesionAsync(Guid sesionId, CancellationToken ct) =>
        await db.SesionesAcompanamiento.AsNoTracking().Where(s => s.Id == sesionId).Select(s => (Guid?)s.CiudadanoId).SingleOrDefaultAsync(ct) is { } id
            ? await PorCiudadanoAsync(id, ct)
            : null;

    public async Task<FichaCiudadano?> PorCiudadanoAsync(Guid ciudadanoId, CancellationToken ct)
    {
        var fila = await (from u in db.Users.AsNoTracking()
                          join perfil in db.PerfilesCiudadano.AsNoTracking() on u.Id equals perfil.UsuarioId into perfiles
                          from perfil in perfiles.DefaultIfEmpty()
                          where u.Id == ciudadanoId
                          select new { u.Nombres, u.Apellidos, u.PhoneNumber, u.PhoneNumberConfirmed, Perfil = perfil })
                         .SingleOrDefaultAsync(ct);
        if (fila is null)
            return null;

        var p = fila.Perfil;
        return new FichaCiudadano(
            $"{fila.Nombres} {fila.Apellidos}",
            fila.PhoneNumber,
            fila.PhoneNumberConfirmed,
            p?.FechaNacimiento,
            p?.FechaNacimiento is { } nacimiento ? Edad(nacimiento) : null,
            p?.NumeroDocumento is { } numero ? $"{DatosCiudadano.NombreTipo(p.TipoDocumento!)} {numero}" : null,
            p?.Direccion,
            p?.ContactoNombre,
            p?.ContactoParentesco,
            p?.ContactoTelefono);
    }

    private int Edad(DateOnly nacimiento)
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById(configuracion["Panel:ZonaHoraria"] ?? "America/Lima");
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona).DateTime);
        var edad = hoy.Year - nacimiento.Year;
        return nacimiento.AddYears(edad) > hoy ? edad - 1 : edad;
    }
}
