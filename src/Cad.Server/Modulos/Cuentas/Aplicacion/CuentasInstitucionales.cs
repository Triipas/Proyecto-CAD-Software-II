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
// Datos del personal (PO, 22/10/2026): DNI obligatorio y único; código de personal generado por el sistema; teléfono
// obligatorio para el sereno (la central lo llama durante un despacho). La contraseña que fija el administrador (alta o
// restablecimiento) es provisional: la cuenta debe cambiarla al ingresar antes de operar.

public sealed record DatosNuevaCuenta(string Nombres, string Apellidos, string Correo, string? Telefono, string Rol, string Contrasena, string? Dni);

// NuevaContrasena vacía = no cambiar la contraseña.
public sealed record DatosModificacionCuenta(string Nombres, string Apellidos, string Correo, string? Telefono, string? NuevaContrasena, string? Dni);

public sealed record CuentaInstitucional(
    Guid Id, string Nombres, string Apellidos, string Correo, string? Telefono, string Rol, bool Activa,
    string? Dni = null, string? CodigoPersonal = null, bool DebeCambiarContrasena = false)
{
    // Cuentas anteriores al 22/10/2026 o sin completar: el administrador las completa desde «Editar».
    public bool DatosIncompletos => Dni is null || CodigoPersonal is null || (Rol == Roles.Sereno && Telefono is null);
}

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
        ReglasCuenta.ValidarDni(errores, datos.Dni);
        ReglasCuenta.ValidarTelefono(errores, datos.Telefono, obligatorio: datos.Rol == Roles.Sereno);
        if (!RolesInstitucionales.Todos.Contains(datos.Rol))
            errores["rol"] = ["Elige el rol: Operador o Sereno."];
        ReglasCuenta.ValidarContrasena(errores, "contrasena", datos.Contrasena, obligatoria: true);
        if (errores.Count > 0)
            return new ResultadoCuentaInstitucional.Invalido(errores);

        var correo = ReglasCuenta.NormalizarCorreo(datos.Correo);
        if (await usuarios.FindByEmailAsync(correo) is not null)
            return new ResultadoCuentaInstitucional.Duplicado();
        var dni = ReglasCuenta.NormalizarDni(datos.Dni);
        if (await db.Users.AnyAsync(u => u.Dni == dni, ct))
            return DniDuplicado();

        // El código es correlativo por rol; si otra alta simultánea toma el mismo, el índice único lo rechaza y se
        // vuelve a intentar con el siguiente.
        for (var intento = 0; ; intento++)
        {
            var usuario = new Usuario
            {
                Id = Guid.CreateVersion7(),
                UserName = correo,
                Email = correo,
                Nombres = datos.Nombres.Trim(),
                Apellidos = datos.Apellidos.Trim(),
                PhoneNumber = ReglasCuenta.NormalizarTelefono(datos.Telefono),
                Dni = dni,
                CodigoPersonal = await SiguienteCodigoAsync(datos.Rol, ct),
                DebeCambiarContrasena = true,
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
                await transaccion.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                switch (ex.IndiceViolado())
                {
                    case CadDbContext.IndiceCodigoPersonal when intento < 4:
                        continue;
                    case CadDbContext.IndiceDni:
                        return DniDuplicado();
                    default:
                        return new ResultadoCuentaInstitucional.Duplicado();
                }
            }
        }
    }

    private async Task<string> SiguienteCodigoAsync(string rol, CancellationToken ct)
    {
        var prefijo = ReglasCuenta.PrefijoCodigo(rol);
        var existentes = await db.Users.AsNoTracking()
            .Where(u => u.CodigoPersonal != null && u.CodigoPersonal.StartsWith(prefijo))
            .Select(u => u.CodigoPersonal!).ToListAsync(ct);
        return ReglasCuenta.SiguienteCodigo(prefijo, existentes);
    }

    internal static ResultadoCuentaInstitucional DniDuplicado() =>
        new ResultadoCuentaInstitucional.Invalido(new() { ["dni"] = [ReglasCuenta.MensajeDniDuplicado] });
}

public sealed class ModificarCuentaInstitucional(CadDbContext db, UserManager<Usuario> usuarios, ConsultarCuentasInstitucionales consultar)
{
    public async Task<ResultadoCuentaInstitucional> EjecutarAsync(Guid id, DatosModificacionCuenta datos, CancellationToken ct)
    {
        // Solo cuentas de operador o sereno: el resto no existe para esta operación.
        if (await consultar.ObtenerAsync(id, ct) is not { } cuenta)
            return new ResultadoCuentaInstitucional.NoEncontrada();

        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarNombres(errores, "nombres", "Los nombres", datos.Nombres);
        ReglasCuenta.ValidarNombres(errores, "apellidos", "Los apellidos", datos.Apellidos);
        ReglasCuenta.ValidarCorreo(errores, datos.Correo);
        ReglasCuenta.ValidarDni(errores, datos.Dni);
        ReglasCuenta.ValidarTelefono(errores, datos.Telefono, obligatorio: cuenta.Rol == Roles.Sereno);
        ReglasCuenta.ValidarContrasena(errores, "nuevaContrasena", datos.NuevaContrasena, obligatoria: false);
        if (errores.Count > 0)
            return new ResultadoCuentaInstitucional.Invalido(errores);

        var correo = ReglasCuenta.NormalizarCorreo(datos.Correo);
        var otra = await usuarios.FindByEmailAsync(correo);
        if (otra is not null && otra.Id != id)
            return new ResultadoCuentaInstitucional.Duplicado();
        var dni = ReglasCuenta.NormalizarDni(datos.Dni);
        if (await db.Users.AnyAsync(u => u.Dni == dni && u.Id != id, ct))
            return CrearCuentaInstitucional.DniDuplicado();

        var usuario = (await usuarios.FindByIdAsync(id.ToString()))!;

        // Datos y, si se pide, contraseña en una sola transacción: o se guarda todo o nada.
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        try
        {
            usuario.Nombres = datos.Nombres.Trim();
            usuario.Apellidos = datos.Apellidos.Trim();
            usuario.PhoneNumber = ReglasCuenta.NormalizarTelefono(datos.Telefono);
            usuario.Dni = dni;
            usuario.Email = correo;
            usuario.UserName = correo;
            // Una contraseña restablecida por el administrador vuelve a ser provisional.
            if (!string.IsNullOrEmpty(datos.NuevaContrasena))
                usuario.DebeCambiarContrasena = true;

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
            return ex.IndiceViolado() == CadDbContext.IndiceDni
                ? CrearCuentaInstitucional.DniDuplicado()
                : new ResultadoCuentaInstitucional.Duplicado();
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

            // 27/10/2026: un sereno desactivado deja de estar operativo, y el cambio queda en su historial (analítica de
            // disponibilidad). Al reactivarse, vuelve a activar su estado desde la app.
            if (await db.EstadosSereno.SingleOrDefaultAsync(e => e.SerenoId == id, ct) is { Operativo: true } estado)
            {
                estado.Cambiar(false, ahora);
                db.CambiosEstadoSereno.Add(Modulos.Serenos.Dominio.CambioEstadoSereno.Registrar(estado));
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
        f.Usuario.Id, f.Usuario.Nombres, f.Usuario.Apellidos, f.Usuario.Email!, f.Usuario.PhoneNumber, f.Rol, f.Usuario.Activo,
        f.Usuario.Dni, f.Usuario.CodigoPersonal, f.Usuario.DebeCambiarContrasena);
}
