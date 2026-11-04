namespace Cad.Server.Modulos.Cuentas.Dominio;

// S6-T05 — actor de las acciones automáticas (admisión automática, HU45; plan de asistencia con IA, 7.5). Los eventos y las
// decisiones guardan su responsable como clave foránea a Usuarios, así que el sistema es un usuario real: se siembra en la
// migración con identificador fijo, sin contraseña, sin rol y desactivado. No puede iniciar sesión y, sin rol, no aparece en
// las listas de cuentas, operadores ni serenos (todas filtran por rol).
public static class UsuarioSistema
{
    public static readonly Guid Id = new("0199d6a0-0000-7000-8000-0000000000a1");

    public const string Nombres = "Sistema";
    public const string Apellidos = "CAD (automático)";

    // Nombre de usuario que nadie puede escribir en el ingreso (no es un correo ni un celular).
    public const string NombreUsuario = "sistema.cad";

    public static bool Es(Guid usuarioId) => usuarioId == Id;
}
