using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AnaliticaPatrones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PercepcionZonas",
                columns: table => new
                {
                    Hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fila = table.Column<long>(type: "bigint", nullable: false),
                    Columna = table.Column<long>(type: "bigint", nullable: false),
                    Recorridos = table.Column<int>(type: "integer", nullable: false),
                    Avisos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PercepcionZonas", x => new { x.Hora, x.Fila, x.Columna });
                    table.CheckConstraint("CK_PercepcionZonas_Conteos", "\"Recorridos\" >= 0 AND \"Avisos\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionesReporte_DecididaEn",
                table: "DecisionesReporte",
                column: "DecididaEn");

            migrationBuilder.CreateIndex(
                name: "IX_Casos_RegistradoEn",
                table: "Casos",
                column: "RegistradoEn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PercepcionZonas");

            migrationBuilder.DropIndex(
                name: "IX_DecisionesReporte_DecididaEn",
                table: "DecisionesReporte");

            migrationBuilder.DropIndex(
                name: "IX_Casos_RegistradoEn",
                table: "Casos");
        }
    }
}
