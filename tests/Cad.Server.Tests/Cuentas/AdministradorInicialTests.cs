using Cad.Server.Modulos.Cuentas.Aplicacion;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Tests.Infraestructura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cad.Server.Tests.Cuentas;

// DP02 (supuesto aprobado): administrador inicial sembrado desde configuración.
[Collection(ColeccionServidor.Nombre)]
public class AdministradorInicialTests(FabricaCad fabrica)
{
    [Fact]
    public async Task AlArrancar_CreaElAdministradorActivoConSuRol()
    {
        var (activo, roles) = await fabrica.ConServiciosAsync(async servicios =>
        {
            var usuarios = servicios.GetRequiredService<UserManager<Usuario>>();
            var admin = await usuarios.FindByEmailAsync(fabrica.AdminCorreo);
            Assert.NotNull(admin);
            return (admin.Activo, await usuarios.GetRolesAsync(admin));
        });

        Assert.True(activo);
        Assert.Equal(new[] { Roles.Administrador }, roles);
    }

    [Fact]
    public async Task EjecutarDeNuevo_NoDuplicaNiModificaElAdministrador()
    {
        var hashAntes = await HashDelAdministradorAsync();

        await fabrica.ConServiciosAsync(async servicios =>
        {
            await servicios.GetRequiredService<SembrarAdministrador>().EjecutarAsync();
            return true;
        });

        var cuentas = await fabrica.ConBaseDatosAsync(db =>
            db.Users.CountAsync(u => u.NormalizedEmail == fabrica.AdminCorreo.ToUpperInvariant()));
        Assert.Equal(1, cuentas);
        Assert.Equal(hashAntes, await HashDelAdministradorAsync());
    }

    private Task<string?> HashDelAdministradorAsync() => fabrica.ConBaseDatosAsync(db => db.Users.AsNoTracking()
        .Where(u => u.NormalizedEmail == fabrica.AdminCorreo.ToUpperInvariant())
        .Select(u => u.PasswordHash)
        .SingleAsync());
}
