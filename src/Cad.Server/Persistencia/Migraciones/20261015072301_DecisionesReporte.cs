using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DecisionesReporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionesReporte",
                columns: table => new
                {
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Destino = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Accion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecididaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionesReporte", x => x.CasoId);
                    table.CheckConstraint("CK_DecisionesReporte_Derivacion", "(\"Tipo\" = 'Derivacion' AND \"Destino\" IS NOT NULL AND \"Accion\" IS NOT NULL AND length(btrim(\"Destino\")) > 0 AND length(btrim(\"Accion\")) > 0) OR (\"Tipo\" IN ('Admision', 'Descarte') AND \"Destino\" IS NULL AND \"Accion\" IS NULL)");
                    table.CheckConstraint("CK_DecisionesReporte_Motivo", "length(btrim(\"Motivo\")) > 0");
                    table.ForeignKey(
                        name: "FK_DecisionesReporte_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DecisionesReporte_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionesReporte_OperadorId",
                table: "DecisionesReporte",
                column: "OperadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionesReporte");
        }
    }
}
