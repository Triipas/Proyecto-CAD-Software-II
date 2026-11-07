using Cad.Contratos.Cuentas;

namespace Cad.Server.Tests.Infraestructura;

// Registro único (06/11/2026): toda cuenta nueva trae documento, fecha de nacimiento y contacto de emergencia. Datos ficticios; el
// DNI cambia en cada llamada porque es único entre cuentas.
public static class RegistroDePrueba
{
    public static string DniNuevo() => Random.Shared.Next(10_000_000, 99_999_999).ToString();

    public static SolicitudRegistroCelular Solicitud(string token, string nombres, string apellidos, string pin, bool aceptaTerminos,
        string? dni = null) =>
        new(token, nombres, apellidos, pin, aceptaTerminos, DatosCiudadano.Dni, dni ?? DniNuevo(), new DateOnly(1990, 5, 17), null,
            "Marta Ficticia", "Madre", "911222333");
}
