using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// HU04 — Crear, consultar y modificar cuentas institucionales (S1-T10, S1-T11).
// Alcance: solo cuentas de operador y sereno (RF20 no cubre ciudadanos ni administradores).
// Datos (criterio de DP01 aplicado al personal, 12/10/2026): nombres, apellidos, correo
// (identificador) y contraseña inicial obligatorios; teléfono opcional. El rol se fija al crear:
// ningún criterio pide cambiarlo y hacerlo dejaría sesiones abiertas con el rol anterior (DP03).

public sealed record DatosNuevaCuenta(string Nombres, string Apellidos, string Correo, string? Telefono, string Rol, string Contrasena);

// NuevaContrasena vacía = no cambiar la contraseña.
public sealed record DatosModificacionCuenta(string Nombres, string Apellidos, string Correo, string? Telefono, string? NuevaContrasena);

public sealed record CuentaInstitucional(
    Guid Id, string Nombres, string Apellidos, string Correo, string? Telefono, string Rol, bool Activa);

public abstract record ResultadoCuentaInstitucional
{
    public sealed record Correcto(Guid Id) : ResultadoCuentaInstitucional;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoCuentaInstitucional;
    public sealed record Duplicado : ResultadoCuentaInstitucional;
    public sealed record NoEncontrada : ResultadoCuentaInstitucional;
}

public static class RolesInstitucionales
{
    public static readonly string[] Todos = [Roles.Operador, Roles.Sereno];
}

public sealed class CrearCuentaInstitucional(CadDbContext db, UserManager<Usuario> usuarios, TimeProvider reloj)
{
    public async Task<ResultadoCuentaInstitucional> EjecutarAsync(DatosNuevaCuenta datos, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarNombres(errores, "nombres", "Los nombres", datos.Nombres);
        ReglasCuenta.ValidarNombres(errores, "apellidos", "Los apellidos", datos.Apellidos);
        ReglasCuenta.ValidarCorreo(errores, datos.Correo);
        ReglasCuenta.ValidarTelefono(errores, datos.Telefono, obligatorio: false);
        if (!RolesInstitucionales.Todos.Contains(datos.Rol))
            errores["rol"] = ["Elige el rol: Operador o Sereno."];
        ReglasCuenta.ValidarContrasena(errores, "contrasena", datos.Contrasena, obligatoria: true);
        if (errores.Count > 0)
            return new ResultadoCuentaInstitucional.Invalido(errores);

        var correo = ReglasCuenta.NormalizarCorreo(datos.Correo);
        if (await usuarios.FindByEmailAsync(correo) is not null)
            return new ResultadoCuentaInstitucional.Duplicado();

        var usuario = new Usuario
        {
            Id = Guid.CreateVersion7(),
            UserName = correo,
            Email = correo,
            Nombres = datos.Nombres.Trim(),
            Apellidos = datos.Apellidos.Trim(),
            PhoneNumber = ReglasCuenta.NormalizarTelefono(datos.Telefono),
            Activo = true,
            CreadoEn = reloj.GetUtcNow(),
        };

        // Cuenta y rol en una sola transacción (CA03: sin registros parciales).
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var creacion = await usuarios.CreateAsync(usuario, datos.Contrasena);
            if (!creacion.Succeeded)
            {
                return ReglasCuenta.EsDuplicado(creacion)
                    ? new ResultadoCuentaInstitucional.Duplicado()
                    : new ResultadoCuentaInstitucional.Invalido(ReglasCuenta.ErroresDeIdentity(creacion, "contrasena"));
            }

            var asignacion = await usuarios.AddToRoleAsync(usuario, datos.Rol);
            if (!asignacion.Succeeded)
                throw new InvalidOperationException("No se pudo asignar el rol a la cuenta institucional nueva.");

            await transaccion.CommitAsync(ct);
            return new ResultadoCuentaInstitucional.Correcto(usuario.Id);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            return new ResultadoCuentaInstitucional.Duplicado();
        }
    }
}

public sealed class ModificarCuentaInstitucional(CadDbContext db, UserManager<Usuario> usuarios, ConsultarCuentasInstitucionales consultar)
{
    public async Task<ResultadoCuentaInstitucional> EjecutarAsync(Guid id, DatosModificacionCuenta datos, CancellationToken ct)
    {
        // Solo cuentas de operador o sereno: el resto no existe para esta operación.
        if (await consultar.ObtenerAsync(id, ct) is null)
            return new ResultadoCuentaInstitucional.NoEncontrada();

        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarNombres(errores, "nombres", "Los nombres", datos.Nombres);
        ReglasCuenta.ValidarNombres(errores, "apellidos", "Los apellidos", datos.Apellidos);
        ReglasCuenta.ValidarCorreo(errores, datos.Correo);
        ReglasCuenta.ValidarTelefono(errores, datos.Telefono, obligatorio: false);
        ReglasCuenta.ValidarContrasena(errores, "nuevaContrasena", datos.NuevaContrasena, obligatoria: false);
        if (errores.Count > 0)
            return new ResultadoCuentaInstitucional.Invalido(errores);

        var correo = ReglasCuenta.NormalizarCorreo(datos.Correo);
        var otra = await usuarios.FindByEmailAsync(correo);
        if (otra is not null && otra.Id != id)
            return new ResultadoCuentaInstitucional.Duplicado();

        var usuario = (await usuarios.FindByIdAsync(id.ToString()))!;

        // Datos y, si se pide, contraseña en una sola transacción: o se guarda todo o nada.
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        try
        {
            usuario.Nombres = datos.Nombres.Trim();
            usuario.Apellidos = datos.Apellidos.Trim();
            usuario.PhoneNumber = ReglasCuenta.NormalizarTelefono(datos.Telefono);
            usuario.Email = correo;
            usuario.UserName = correo;

            var actualizacion = await usuarios.UpdateAsync(usuario);
            if (!actualizacion.Succeeded)
            {
                return ReglasCuenta.EsDuplicado(actualizacion)
                    ? new ResultadoCuentaInstitucional.Duplicado()
                    : new ResultadoCuentaInstitucional.Invalido(ReglasCuenta.ErroresDeIdentity(actualizacion, "nuevaContrasena"));
            }

            if (!string.IsNullOrEmpty(datos.NuevaContrasena))
            {
                // El hash se genera con el mismo PasswordHasher que el registro (RNF01, CA07).
                var quitar = await usuarios.RemovePasswordAsync(usuario);
                var poner = quitar.Succeeded ? await usuarios.AddPasswordAsync(usuario, datos.NuevaContrasena) : quitar;
                if (!poner.Succeeded)
                    return new ResultadoCuentaInstitucional.Invalido(ReglasCuenta.ErroresDeIdentity(poner, "nuevaContrasena"));
            }

            await transaccion.CommitAsync(ct);
            return new ResultadoCuentaInstitucional.Correcto(usuario.Id);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            return new ResultadoCuentaInstitucional.Duplicado();
        }
    }
}

// HU05 — Desactivar una cuenta institucional (S4-T12). Solo cambia el estado: la cuenta, su rol y todos los registros
// vinculados con ella se conservan (CA03), y el inicio de sesión la rechaza a partir de ese momento (CA02, en IniciarSesion).
// DP03 (decisión del PO del 16/10/2026, CA04): la sesión abierta se cierra y la asignación vigente se cancela. La cuenta
// recibe un sello de seguridad nuevo, que el servidor comprueba en cada solicitud (Autenticacion), así que el JWT de la app
// y la cookie del panel dejan de valer en el acto. Una asignación vigente termina como cancelada y el caso vuelve a
// «Pendiente» para asignarlo a otro sereno; todo en la misma transacción que la desactivación.
public sealed class DesactivarCuentaInstitucional(CadDbContext db, UserManager<Usuario> usuarios, ConsultarCuentasInstitucionales consultar,
    TimeProvider reloj, Compartido.INotificador notificador, ILogger<DesactivarCuentaInstitucional> registro)
{
    public async Task<ResultadoCuentaInstitucional> EjecutarAsync(Guid id, Guid administradorId, CancellationToken ct)
    {
        // Solo cuentas de operador o sereno (RF20): el resto no existe para esta operación.
        if (await consultar.ObtenerAsync(id, ct) is null)
            return new ResultadoCuentaInstitucional.NoEncontrada();

        for (var intento = 0; ; intento++)
        {
            var usuario = (await usuarios.FindByIdAsync(id.ToString()))!;
            // Desactivar una cuenta ya desactivada no cambia nada (un reenvío o dos administradores a la vez).
            if (!usuario.Activo)
                return new ResultadoCuentaInstitucional.Correcto(id);

            // DP03: la asignación vigente del sereno se cancela y su caso vuelve a «Pendiente».
            var ahora = reloj.GetUtcNow();
            var asignacion = await db.Asignaciones.SingleOrDefaultAsync(a => a.SerenoId == id && a.Vigente, ct);
            Caso? caso = null;
            if (asignacion is not null)
            {
                caso = await db.Casos.SingleAsync(c => c.Id == asignacion.CasoId, ct);
                asignacion.Cancelar(ahora);
                caso.LiberarPorCancelacion();
                db.EventosCaso.Add(EventoCaso.Registrar(caso, TipoEventoCaso.AsignacionCancelada, administradorId, VisibilidadEvento.Interna, ahora));
            }

            usuario.Activo = false;
            // Sello nuevo = sesiones abiertas revocadas. UpdateSecurityStampAsync guarda también la asignación y el caso
            // (mismo contexto de datos): una sola transacción.
            var actualizacion = await usuarios.UpdateSecurityStampAsync(usuario);
            if (actualizacion.Succeeded)
            {
                registro.LogInformation("Cuenta institucional {UsuarioId} desactivada; sesiones revocadas{Asignacion}.", id,
                    caso is null ? "" : $"; asignación del caso {caso.Id} cancelada");
                if (caso is not null)
                    await AvisarAsync(caso.Id, id);
                return new ResultadoCuentaInstitucional.Correcto(id);
            }
            // Otra solicitud modificó la cuenta entre la lectura y la escritura (sello de concurrencia de Identity):
            // se vuelve a leer y a evaluar.
            if (intento < 2 && actualizacion.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            {
                db.ChangeTracker.Clear();
                continue;
            }
            throw new InvalidOperationException("No se pudo desactivar la cuenta institucional.");
        }
    }

    // Después de confirmar: la central muestra el caso de nuevo como pendiente y al sereno sin asignación.
    private async Task AvisarAsync(Guid casoId, Guid serenoId)
    {
        try
        {
            await notificador.CasoActualizadoAsync(casoId, CancellationToken.None);
            await notificador.SerenoActualizadoAsync(serenoId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            registro.LogError(ex, "No se pudo avisar a la central de la asignación cancelada del caso {CasoId}; se mostrará al resincronizar.", casoId);
        }
    }
}

public sealed class ConsultarCuentasInstitucionales(CadDbContext db)
{
    // CA04: cada cuenta institucional con su rol y su estado.
    // Se filtra y ordena sobre las entidades y se proyecta al final, para que EF Core lo traduzca a SQL.
    public async Task<IReadOnlyList<CuentaInstitucional>> ListarAsync(CancellationToken ct) =>
        (await Filas().OrderBy(f => f.Usuario.Apellidos).ThenBy(f => f.Usuario.Nombres).ToListAsync(ct))
            .Select(ACuenta).ToList();

    public async Task<CuentaInstitucional?> ObtenerAsync(Guid id, CancellationToken ct) =>
        await Filas().Where(f => f.Usuario.Id == id).SingleOrDefaultAsync(ct) is { } fila ? ACuenta(fila) : null;

    // Inicialización por propiedades (no por constructor): EF Core puede traducir filtros y orden sobre ella.
    private sealed class Fila
    {
        public required Usuario Usuario { get; init; }
        public required string Rol { get; init; }
    }

    private IQueryable<Fila> Filas() =>
        from usuario in db.Users.AsNoTracking()
        join usuarioRol in db.UserRoles on usuario.Id equals usuarioRol.UserId
        join rol in db.Roles on usuarioRol.RoleId equals rol.Id
        where rol.Name == Roles.Operador || rol.Name == Roles.Sereno
        select new Fila { Usuario = usuario, Rol = rol.Name! };

    private static CuentaInstitucional ACuenta(Fila f) => new(
        f.Usuario.Id, f.Usuario.Nombres, f.Usuario.Apellidos, f.Usuario.Email!, f.Usuario.PhoneNumber, f.Rol, f.Usuario.Activo);
}
