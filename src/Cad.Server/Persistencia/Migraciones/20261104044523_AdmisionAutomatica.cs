using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AdmisionAutomatica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S6-T09: búsqueda de texto completo en español sobre las descripciones (ejemplos del prompt). Índice de expresión: EF no lo modela.
            migrationBuilder.Sql("CREATE INDEX \"IX_DetallesReporte_Descripcion_Texto\" ON \"DetallesReporte\" USING GIN (to_tsvector('spanish', \"Descripcion\"));");

            migrationBuilder.CreateTable(
                name: "EvaluacionesAdmision",
                columns: table => new
                {
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Elegible = table.Column<bool>(type: "boolean", nullable: false),
                    Condicion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ModeloVerificador = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    VerificadorCoincide = table.Column<bool>(type: "boolean", nullable: true),
                    CategoriaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prioridad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Motivo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EvaluadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AdmitirDesde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluacionesAdmision", x => x.CasoId);
                    table.CheckConstraint("CK_EvaluacionesAdmision_Elegible", "(\"Elegible\" AND \"Condicion\" IS NULL) OR (NOT \"Elegible\" AND \"Condicion\" IS NOT NULL AND \"AdmitirDesde\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_EvaluacionesAdmision_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvaluacionesAdmision_CategoriasIncidencia_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "CategoriasIncidencia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluacionesAdmision_AdmitirDesde",
                table: "EvaluacionesAdmision",
                column: "AdmitirDesde",
                filter: "\"AdmitirDesde\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluacionesAdmision_CategoriaId",
                table: "EvaluacionesAdmision",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluacionesAdmision_EvaluadaEn",
                table: "EvaluacionesAdmision",
                column: "EvaluadaEn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_DetallesReporte_Descripcion_Texto\";");

            migrationBuilder.DropTable(
                name: "EvaluacionesAdmision");
        }
    }
}
