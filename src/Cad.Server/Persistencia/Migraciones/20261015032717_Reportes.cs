using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Reportes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DetallesReporte",
                columns: table => new
                {
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OcurridoAproximadamenteEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FuenteUbicacion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LatitudHecho = table.Column<double>(type: "double precision", nullable: true),
                    LongitudHecho = table.Column<double>(type: "double precision", nullable: true),
                    PrecisionHechoMetros = table.Column<double>(type: "double precision", nullable: true),
                    ReferenciaHecho = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CategoriaOriginalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetallesReporte", x => x.CasoId);
                    table.CheckConstraint("CK_DetallesReporte_Descripcion", "length(btrim(\"Descripcion\")) > 0");
                    table.CheckConstraint("CK_DetallesReporte_Latitud", "\"LatitudHecho\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_DetallesReporte_Longitud", "\"LongitudHecho\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_DetallesReporte_Precision", "\"PrecisionHechoMetros\" > 0");
                    table.CheckConstraint("CK_DetallesReporte_Ubicacion", "(\"FuenteUbicacion\" = 'Texto' AND \"LatitudHecho\" IS NULL AND \"LongitudHecho\" IS NULL AND \"PrecisionHechoMetros\" IS NULL AND \"ReferenciaHecho\" IS NOT NULL AND length(btrim(\"ReferenciaHecho\")) > 0) OR (\"FuenteUbicacion\" IN ('Mapa', 'PosicionActual') AND \"LatitudHecho\" IS NOT NULL AND \"LongitudHecho\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DetallesReporte_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DetallesReporte_CategoriasIncidencia_CategoriaOriginalId",
                        column: x => x.CategoriaOriginalId,
                        principalTable: "CategoriasIncidencia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DetallesReporte_CategoriaOriginalId",
                table: "DetallesReporte",
                column: "CategoriaOriginalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DetallesReporte");
        }
    }
}
