using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class PerfilCiudadano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerfilesCiudadano",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoDocumento = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    FechaNacimiento = table.Column<DateOnly>(type: "date", nullable: true),
                    Direccion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactoNombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContactoParentesco = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ContactoTelefono = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    ActualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerfilesCiudadano", x => x.UsuarioId);
                    table.ForeignKey(
                        name: "FK_PerfilesCiudadano_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerfilesCiudadano_TipoDocumento_NumeroDocumento",
                table: "PerfilesCiudadano",
                columns: new[] { "TipoDocumento", "NumeroDocumento" },
                unique: true,
                filter: "\"NumeroDocumento\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerfilesCiudadano");
        }
    }
}
