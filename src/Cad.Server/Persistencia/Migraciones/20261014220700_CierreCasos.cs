using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CierreCasos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalizadaEn",
                table: "Asignaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CierresCaso",
                columns: table => new
                {
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultadoPublico = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NotasInternas = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: false),
                    CerradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CierresCaso", x => x.CasoId);
                    table.CheckConstraint("CK_CierresCaso_Resultado", "length(btrim(\"ResultadoPublico\")) > 0");
                    table.ForeignKey(
                        name: "FK_CierresCaso_AspNetUsers_ResponsableId",
                        column: x => x.ResponsableId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CierresCaso_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CierresCaso_CategoriasIncidencia_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "CategoriasIncidencia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CierresCaso_CategoriaId",
                table: "CierresCaso",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_CierresCaso_ResponsableId",
                table: "CierresCaso",
                column: "ResponsableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CierresCaso");

            migrationBuilder.DropColumn(
                name: "FinalizadaEn",
                table: "Asignaciones");
        }
    }
}
