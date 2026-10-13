using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class MedicionPresentacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MedicionesPresentacion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AceptadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PresentadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicionesPresentacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicionesPresentacion_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MedicionesPresentacion_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicionesPresentacion_AceptadoEn",
                table: "MedicionesPresentacion",
                column: "AceptadoEn");

            migrationBuilder.CreateIndex(
                name: "IX_MedicionesPresentacion_CasoId_OperadorId",
                table: "MedicionesPresentacion",
                columns: new[] { "CasoId", "OperadorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicionesPresentacion_OperadorId",
                table: "MedicionesPresentacion",
                column: "OperadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicionesPresentacion");
        }
    }
}
