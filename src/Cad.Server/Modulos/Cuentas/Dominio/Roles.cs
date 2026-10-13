namespace Cad.Server.Modulos.Cuentas.Dominio;

public static class Roles
{
    public const string Ciudadano = "Ciudadano";
    public const string Operador = "Operador";
    public const string Sereno = "Sereno";
    public const string Administrador = "Administrador";

    // Identificadores fijos para que la semilla de la migración no cambie entre ejecuciones.
    public static readonly IReadOnlyList<(Guid Id, string Nombre)> Todos =
    [
        (new Guid("0199d6a0-0000-7000-8000-000000000001"), Ciudadano),
        (new Guid("0199d6a0-0000-7000-8000-000000000002"), Operador),
        (new Guid("0199d6a0-0000-7000-8000-000000000003"), Sereno),
        (new Guid("0199d6a0-0000-7000-8000-000000000004"), Administrador),
    ];
}
