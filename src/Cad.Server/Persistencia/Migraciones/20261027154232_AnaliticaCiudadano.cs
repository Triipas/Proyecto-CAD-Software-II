using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AnaliticaCiudadano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "Motivos",
                table: "EvaluacionesCaso",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "ActividadesCiudadano",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dia = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActividadesCiudadano", x => new { x.UsuarioId, x.Dia });
                    table.ForeignKey(
                        name: "FK_ActividadesCiudadano_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_EvaluacionesCaso_Motivos",
                table: "EvaluacionesCaso",
                sql: "\"Motivos\" <@ ARRAY['LlegaronRapido', 'BuenTrato', 'Resolvieron', 'MeInformaron', 'Tardaron', 'MalTrato', 'NoResolvieron', 'NoMeInformaron']::text[]");

            migrationBuilder.CreateIndex(
                name: "IX_ActividadesCiudadano_Dia",
                table: "ActividadesCiudadano",
                column: "Dia");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActividadesCiudadano");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EvaluacionesCaso_Motivos",
                table: "EvaluacionesCaso");

            migrationBuilder.DropColumn(
                name: "Motivos",
                table: "EvaluacionesCaso");
        }
    }
}
