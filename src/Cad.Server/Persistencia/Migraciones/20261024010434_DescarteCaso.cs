using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DescarteCaso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DescartesCaso",
                columns: table => new
                {
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EstadoPrevio = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DescartadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DescartesCaso", x => x.CasoId);
                    table.CheckConstraint("CK_DescartesCaso_Motivo", "\"Motivo\" IS NOT NULL AND length(btrim(\"Motivo\")) > 0");
                    table.ForeignKey(
                        name: "FK_DescartesCaso_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DescartesCaso_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DescartesCaso_OperadorId",
                table: "DescartesCaso",
                column: "OperadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DescartesCaso");
        }
    }
}
