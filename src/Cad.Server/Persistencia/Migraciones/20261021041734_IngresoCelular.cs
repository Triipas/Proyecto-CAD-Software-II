using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class IngresoCelular : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IntentosPinFallidos",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PinHash",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TerminosAceptadosEn",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminosVersion",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DispositivosCiudadano",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LlaveHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VinculadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispositivosCiudadano", x => x.UsuarioId);
                    table.ForeignKey(
                        name: "FK_DispositivosCiudadano_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerificacionesCelular",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Celular = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    Simulado = table.Column<bool>(type: "boolean", nullable: false),
                    CodigoHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiraEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    VerificadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TokenExpiraEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TokenUsadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificacionesCelular", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VerificacionesCelular_Celular_CreadaEn",
                table: "VerificacionesCelular",
                columns: new[] { "Celular", "CreadaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificacionesCelular_TokenHash",
                table: "VerificacionesCelular",
                column: "TokenHash",
                unique: true,
                filter: "\"TokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DispositivosCiudadano");

            migrationBuilder.DropTable(
                name: "VerificacionesCelular");

            migrationBuilder.DropColumn(
                name: "IntentosPinFallidos",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PinHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TerminosAceptadosEn",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TerminosVersion",
                table: "AspNetUsers");
        }
    }
}
