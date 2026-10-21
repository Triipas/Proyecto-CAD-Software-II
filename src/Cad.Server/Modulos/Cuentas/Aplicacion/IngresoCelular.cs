using Cad.Contratos.Cuentas;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cad.Server.Modulos.Cuentas.Aplicacion;

// ADR-08 (DP25): solo en desarrollo, un número con cuenta se puede volver a registrar.
public sealed class OpcionesCuentasCiudadano
{
    public const string Seccion = "Cuentas";

    public bool PermitirReRegistroCelular { get; set; }
    // Versión vigente de los términos que acepta el ciudadano al registrarse.
    public string VersionTerminos { get; set; } = "2026-10-20";
}

public abstract record ResultadoCodigo
{
    public sealed record Enviado(int ReenviarEnSegundos, int ExpiraEnSegundos, string? CodigoDePrueba) : ResultadoCodigo;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoCodigo;
    public sealed record Espera(int Segundos) : ResultadoCodigo;
    public sealed record LimiteAlcanzado : ResultadoCodigo;
    public sealed record NoDisponible : ResultadoCodigo;
}

public abstract record ResultadoVerificacion
{
    public sealed record Verificado(string Token, bool CuentaExiste, bool PuedeRegistrarDeNuevo) : ResultadoVerificacion;
    public sealed record Incorrecto(int IntentosRestantes) : ResultadoVerificacion;
    // Sin código vigente: expiró, se agotaron los intentos o nunca se pidió.
    public sealed record SinCodigoVigente : ResultadoVerificacion;
}

// HU01 v2 (ADR-08): envío y validación del código que prueba que el celular es de quien lo escribe.
public sealed class CodigosCelular(
    CadDbContext db,
    UserManager<Usuario> usuarios,
    IVerificacionSms sms,
    IOptions<OpcionesCodigos> opciones,
    IOptions<OpcionesCuentasCiudadano> cuentas,
    TimeProvider reloj,
    ILogger<CodigosCelular> registro)
{
    private OpcionesCodigos O => opciones.Value;

    public async Task<ResultadoCodigo> EnviarAsync(string? celularEscrito, CancellationToken ct)
    {
        var celular = ReglasCuenta.NormalizarCelular(celularEscrito);
        if (!ReglasCuenta.EsCelularValido(celular))
            return new ResultadoCodigo.Invalido(new() { ["celular"] = [ReglasCuenta.MensajeCelular] });

        var ahora = reloj.GetUtcNow();
        var recientes = await db.VerificacionesCelular
            .Where(v => v.Celular == celular && v.CreadaEn > ahora.AddHours(-1))
            .Select(v => v.CreadaEn).ToListAsync(ct);
        if (recientes.Count > 0)
        {
            var espera = recientes.Max().AddSeconds(O.EsperaReenvioSegundos) - ahora;
            if (espera > TimeSpan.Zero)
                return new ResultadoCodigo.Espera((int)Math.Ceiling(espera.TotalSeconds));
        }
        if (recientes.Count >= O.MaximoEnviosPorHora)
            return new ResultadoCodigo.LimiteAlcanzado();

        string? codigoSimulado = null;
        if (UsaSimulado(celular))
        {
            codigoSimulado = Secretos.CodigoNumerico(6);
        }
        else if (!sms.Configurada || !await EnviarSmsAsync(celular, ct))
        {
            return new ResultadoCodigo.NoDisponible();
        }

        db.VerificacionesCelular.Add(VerificacionCelular.Crear(celular, codigoSimulado, ahora, TimeSpan.FromMinutes(O.VigenciaMinutos)));
        await db.SaveChangesAsync(ct);

        return new ResultadoCodigo.Enviado(O.EsperaReenvioSegundos, O.VigenciaMinutos * 60,
            O.MostrarCodigoSimulado ? codigoSimulado : null);
    }

    public async Task<ResultadoVerificacion> VerificarAsync(string? celularEscrito, string? codigo, CancellationToken ct)
    {
        var celular = ReglasCuenta.NormalizarCelular(celularEscrito);
        var ahora = reloj.GetUtcNow();
        var verificacion = await db.VerificacionesCelular
            .Where(v => v.Celular == celular)
            .OrderByDescending(v => v.CreadaEn)
            .FirstOrDefaultAsync(ct);
        if (verificacion is null || !verificacion.Vigente(ahora) || verificacion.Intentos >= O.MaximoIntentos)
            return new ResultadoVerificacion.SinCodigoVigente();

        verificacion.RegistrarIntento();
        var codigoLimpio = (codigo ?? "").Trim();
        var correcto = codigoLimpio.Length == 6 && codigoLimpio.All(char.IsAsciiDigit) && (verificacion.Simulado
            ? verificacion.CodigoSimuladoCoincide(codigoLimpio)
            : await ComprobarSmsAsync(celular, codigoLimpio, ct));

        if (!correcto)
        {
            await db.SaveChangesAsync(ct);
            var restantes = O.MaximoIntentos - verificacion.Intentos;
            return restantes > 0 ? new ResultadoVerificacion.Incorrecto(restantes) : new ResultadoVerificacion.SinCodigoVigente();
        }

        var token = verificacion.MarcarVerificada(ahora, TimeSpan.FromMinutes(O.VigenciaTokenMinutos));
        await db.SaveChangesAsync(ct);

        var existe = await usuarios.FindByNameAsync(celular) is not null;
        return new ResultadoVerificacion.Verificado(token, existe, existe && cuentas.Value.PermitirReRegistroCelular);
    }

    // Verificación de un token aún no usado ni vencido; quien lo consume lo marca usado en su misma transacción.
    public async Task<VerificacionCelular?> BuscarTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var hash = Secretos.Hash(token.Trim());
        var verificacion = await db.VerificacionesCelular.SingleOrDefaultAsync(v => v.TokenHash == hash, ct);
        return verificacion is not null && verificacion.TokenUtilizable(reloj.GetUtcNow()) ? verificacion : null;
    }

    private bool UsaSimulado(string celular) =>
        O.PermitirSimulado && (!sms.Configurada || O.PrefijosSimulados.Any(p => p.Length > 0 && celular.StartsWith(p)));

    private async Task<bool> EnviarSmsAsync(string celular, CancellationToken ct)
    {
        try { return await sms.EnviarAsync($"+51{celular}", ct); }
        catch (HttpRequestException) { registro.LogWarning("No se pudo contactar con el proveedor de SMS."); return false; }
    }

    private async Task<bool> ComprobarSmsAsync(string celular, string codigo, CancellationToken ct)
    {
        try { return await sms.ComprobarAsync($"+51{celular}", codigo, ct); }
        catch (HttpRequestException) { registro.LogWarning("No se pudo contactar con el proveedor de SMS."); return false; }
    }
}

public abstract record ResultadoIngresoCelular
{
    public sealed record Correcto(Usuario Usuario, string? LlaveDispositivo) : ResultadoIngresoCelular;
    public sealed record Invalido(Dictionary<string, string[]> Errores) : ResultadoIngresoCelular;
    public sealed record TokenInvalido : ResultadoIngresoCelular;
    public sealed record Duplicado : ResultadoIngresoCelular;
    public sealed record SinCuenta : ResultadoIngresoCelular;
    public sealed record PinIncorrecto(int IntentosRestantes) : ResultadoIngresoCelular;
    public sealed record PinBloqueado : ResultadoIngresoCelular;
    public sealed record CuentaDesactivada : ResultadoIngresoCelular;
    // La llave no corresponde al teléfono vinculado (otro teléfono se vinculó después): hace falta un código.
    public sealed record DispositivoNoVinculado : ResultadoIngresoCelular;
}

// HU01 v2 y HU02 v2 (ADR-08): registro, ingreso y restablecimiento del PIN del ciudadano.
public sealed class IngresoCelular(
    CadDbContext db,
    UserManager<Usuario> usuarios,
    IPasswordHasher<Usuario> hasher,
    CodigosCelular codigos,
    IOptions<OpcionesCuentasCiudadano> cuentas,
    TimeProvider reloj)
{
    public async Task<ResultadoIngresoCelular> RegistrarAsync(SolicitudRegistroCelular s, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarNombres(errores, "nombres", "Los nombres", s.Nombres);
        ReglasCuenta.ValidarNombres(errores, "apellidos", "Los apellidos", s.Apellidos);
        ReglasCuenta.ValidarPin(errores, "pin", s.Pin);
        if (!s.AceptaTerminos)
            errores["aceptaTerminos"] = ["Para crear tu cuenta debes aceptar los términos y la política de privacidad."];
        if (errores.Count > 0)
            return new ResultadoIngresoCelular.Invalido(errores);

        var verificacion = await codigos.BuscarTokenAsync(s.TokenVerificacion, ct);
        if (verificacion is null)
            return new ResultadoIngresoCelular.TokenInvalido();
        var celular = verificacion.Celular;
        var ahora = reloj.GetUtcNow();

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await usuarios.FindByNameAsync(celular) is { } anterior)
            {
                if (!cuentas.Value.PermitirReRegistroCelular)
                    return new ResultadoIngresoCelular.Duplicado();
                await LiberarNumeroAsync(anterior, ct);
            }

            var usuario = new Usuario
            {
                Id = Guid.CreateVersion7(),
                UserName = celular,
                PhoneNumber = celular,
                PhoneNumberConfirmed = true,
                Nombres = s.Nombres.Trim(),
                Apellidos = s.Apellidos.Trim(),
                Activo = true,
                CreadoEn = ahora,
                TerminosVersion = cuentas.Value.VersionTerminos,
                TerminosAceptadosEn = ahora,
            };
            usuario.PinHash = hasher.HashPassword(usuario, s.Pin);

            var creacion = await usuarios.CreateAsync(usuario);
            if (!creacion.Succeeded)
                return ReglasCuenta.EsDuplicado(creacion)
                    ? new ResultadoIngresoCelular.Duplicado()
                    : throw new InvalidOperationException("Identity rechazó la cuenta de ciudadano nueva.");
            var asignacion = await usuarios.AddToRoleAsync(usuario, Roles.Ciudadano);
            if (!asignacion.Succeeded)
                throw new InvalidOperationException("No se pudo asignar el rol Ciudadano a la cuenta nueva.");

            var llave = await VincularAsync(usuario.Id, ahora, ct);
            verificacion.UsarToken(ahora);
            await db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
            return new ResultadoIngresoCelular.Correcto(usuario, llave);
        }
        catch (DbUpdateException ex) when (ex.EsViolacionDeUnicidad())
        {
            return new ResultadoIngresoCelular.Duplicado();
        }
    }

    public async Task<ResultadoIngresoCelular> IngresarAsync(SolicitudIngresoCelular s, CancellationToken ct)
    {
        var celular = ReglasCuenta.NormalizarCelular(s.Celular);
        var conToken = !string.IsNullOrWhiteSpace(s.TokenVerificacion);
        if (!conToken && string.IsNullOrWhiteSpace(s.LlaveDispositivo))
            return new ResultadoIngresoCelular.DispositivoNoVinculado();

        VerificacionCelular? verificacion = null;
        if (conToken)
        {
            verificacion = await codigos.BuscarTokenAsync(s.TokenVerificacion, ct);
            if (verificacion is null || verificacion.Celular != celular)
                return new ResultadoIngresoCelular.TokenInvalido();
        }

        var usuario = await BuscarCiudadanoAsync(celular);
        if (usuario is null)
            return new ResultadoIngresoCelular.SinCuenta();

        if (!conToken)
        {
            var dispositivo = await db.DispositivosCiudadano.SingleOrDefaultAsync(d => d.UsuarioId == usuario.Id, ct);
            if (dispositivo is null || !dispositivo.Coincide(s.LlaveDispositivo))
                return new ResultadoIngresoCelular.DispositivoNoVinculado();
        }

        // El intento se cuenta antes de comprobar el PIN, de forma atómica (peticiones simultáneas incluidas).
        if (!await IntentosPin.ReservarAsync(db, usuario.Id, ct))
            return new ResultadoIngresoCelular.PinBloqueado();
        if (!PinCorrecto(usuario, s.Pin))
        {
            var usados = await IntentosPin.UsadosAsync(db, usuario.Id, ct);
            return usados >= Usuario.MaximoIntentosPin
                ? new ResultadoIngresoCelular.PinBloqueado()
                : new ResultadoIngresoCelular.PinIncorrecto(Usuario.MaximoIntentosPin - usados);
        }
        await IntentosPin.ReiniciarAsync(db, usuario, ct);

        // Como en HU02-CA03: la desactivación solo se informa a quien ya demostró conocer el PIN.
        if (!usuario.Activo)
            return new ResultadoIngresoCelular.CuentaDesactivada();

        var ahora = reloj.GetUtcNow();
        string? llave = null;
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        usuario.IntentosPinFallidos = 0;
        if (verificacion is not null)
        {
            // Teléfono nuevo (DP24): se vincula y el sello nuevo revoca la sesión del anterior.
            llave = await VincularAsync(usuario.Id, ahora, ct);
            verificacion.UsarToken(ahora);
            await usuarios.UpdateSecurityStampAsync(usuario);
        }
        else
        {
            await usuarios.UpdateAsync(usuario);
        }
        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return new ResultadoIngresoCelular.Correcto(usuario, llave);
    }

    public async Task<ResultadoIngresoCelular> RestablecerPinAsync(SolicitudRestablecerPin s, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        ReglasCuenta.ValidarPin(errores, "pinNuevo", s.PinNuevo);
        if (errores.Count > 0)
            return new ResultadoIngresoCelular.Invalido(errores);

        var verificacion = await codigos.BuscarTokenAsync(s.TokenVerificacion, ct);
        if (verificacion is null)
            return new ResultadoIngresoCelular.TokenInvalido();
        var usuario = await BuscarCiudadanoAsync(verificacion.Celular);
        if (usuario is null)
            return new ResultadoIngresoCelular.SinCuenta();
        if (!usuario.Activo)
            return new ResultadoIngresoCelular.CuentaDesactivada();

        var ahora = reloj.GetUtcNow();
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        usuario.PinHash = hasher.HashPassword(usuario, s.PinNuevo);
        usuario.IntentosPinFallidos = 0;
        var llave = await VincularAsync(usuario.Id, ahora, ct);
        verificacion.UsarToken(ahora);
        await usuarios.UpdateSecurityStampAsync(usuario); // el PIN cambió: ninguna sesión anterior sigue valiendo
        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return new ResultadoIngresoCelular.Correcto(usuario, llave);
    }

    private async Task<Usuario?> BuscarCiudadanoAsync(string celular) =>
        await usuarios.FindByNameAsync(celular) is { PinHash: not null } usuario
        && await usuarios.IsInRoleAsync(usuario, Roles.Ciudadano)
            ? usuario
            : null;

    private bool PinCorrecto(Usuario usuario, string? pin) =>
        !string.IsNullOrEmpty(pin) && usuario.PinHash is not null
        && hasher.VerifyHashedPassword(usuario, usuario.PinHash, pin) != PasswordVerificationResult.Failed;

    // DP24: un solo teléfono por cuenta; la fila anterior se reemplaza.
    private async Task<string> VincularAsync(Guid usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        await db.DispositivosCiudadano.Where(d => d.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
        var (dispositivo, llave) = DispositivoCiudadano.Vincular(usuarioId, ahora);
        db.DispositivosCiudadano.Add(dispositivo);
        return llave;
    }

    // DP25, solo en desarrollo: la cuenta anterior se desactiva, cierra su sesión y deja libre el número.
    // Sus casos se conservan.
    private async Task LiberarNumeroAsync(Usuario anterior, CancellationToken ct)
    {
        anterior.UserName = $"liberado-{anterior.Id:N}";
        anterior.Activo = false;
        await db.DispositivosCiudadano.Where(d => d.UsuarioId == anterior.Id).ExecuteDeleteAsync(ct);
        if (await db.PerfilesCiudadano.SingleOrDefaultAsync(p => p.UsuarioId == anterior.Id, ct) is { } perfil)
            perfil.LiberarDocumento(); // la cuenta nueva puede registrar el mismo documento
        var actualizacion = await usuarios.UpdateSecurityStampAsync(anterior);
        if (!actualizacion.Succeeded)
            throw new InvalidOperationException("No se pudo liberar el número de la cuenta anterior.");
    }
}
