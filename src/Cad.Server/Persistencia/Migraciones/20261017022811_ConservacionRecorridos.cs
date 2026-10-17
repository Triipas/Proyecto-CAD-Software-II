using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ConservacionRecorridos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PosicionesEliminadasEn",
                table: "SesionesAcompanamiento",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConservacionesRecorrido",
                columns: table => new
                {
                    SesionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Finalidad = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistradaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConservacionesRecorrido", x => x.SesionId);
                    table.CheckConstraint("CK_ConservacionesRecorrido_Finalidad", "\"Finalidad\" IS NOT NULL AND length(btrim(\"Finalidad\")) > 0");
                    table.ForeignKey(
                        name: "FK_ConservacionesRecorrido_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConservacionesRecorrido_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConservacionesRecorrido_SesionesAcompanamiento_SesionId",
                        column: x => x.SesionId,
                        principalTable: "SesionesAcompanamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConservacionesRecorrido_CasoId",
                table: "ConservacionesRecorrido",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConservacionesRecorrido_OperadorId",
                table: "ConservacionesRecorrido",
                column: "OperadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConservacionesRecorrido");

            migrationBuilder.DropColumn(
                name: "PosicionesEliminadasEn",
                table: "SesionesAcompanamiento");
        }
    }
}
