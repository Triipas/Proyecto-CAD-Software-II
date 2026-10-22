using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DatosPersonal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoPersonal",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DebeCambiarContrasena",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Dni",
                table: "AspNetUsers",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CodigoPersonal",
                table: "AspNetUsers",
                column: "CodigoPersonal",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Dni",
                table: "AspNetUsers",
                column: "Dni",
                unique: true);

            // Las cuentas de personal que ya existen reciben su código por orden de alta (OP-001…, S-001…). El DNI queda
            // vacío: el panel las marca como «Datos incompletos» para que el administrador lo complete. Sin cambio de
            // contraseña obligatorio: ya la usan.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" AS u
                SET "CodigoPersonal" = CASE c.rol WHEN 'Sereno' THEN 'S-' ELSE 'OP-' END
                    || CASE WHEN c.n < 1000 THEN to_char(c.n, 'FM000') ELSE c.n::text END
                FROM (
                    SELECT u2."Id", r."Name" AS rol,
                           row_number() OVER (PARTITION BY r."Name" ORDER BY u2."CreadoEn", u2."Id") AS n
                    FROM "AspNetUsers" u2
                    JOIN "AspNetUserRoles" ur ON ur."UserId" = u2."Id"
                    JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE r."Name" IN ('Operador', 'Sereno')
                ) AS c
                WHERE u."Id" = c."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CodigoPersonal",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_Dni",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CodigoPersonal",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DebeCambiarContrasena",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Dni",
                table: "AspNetUsers");
        }
    }
}
