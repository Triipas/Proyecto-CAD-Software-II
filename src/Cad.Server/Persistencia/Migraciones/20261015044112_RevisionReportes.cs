using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class RevisionReportes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorreccionesCategoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaAnteriorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaNuevaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorregidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorreccionesCategoria", x => x.Id);
                    table.CheckConstraint("CK_CorreccionesCategoria_Cambio", "\"CategoriaAnteriorId\" <> \"CategoriaNuevaId\"");
                    table.ForeignKey(
                        name: "FK_CorreccionesCategoria_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorreccionesCategoria_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorreccionesCategoria_CategoriasIncidencia_CategoriaAnterio~",
                        column: x => x.CategoriaAnteriorId,
                        principalTable: "CategoriasIncidencia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CorreccionesCategoria_CategoriasIncidencia_CategoriaNuevaId",
                        column: x => x.CategoriaNuevaId,
                        principalTable: "CategoriasIncidencia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorreccionesCategoria_CasoId_CorregidaEn",
                table: "CorreccionesCategoria",
                columns: new[] { "CasoId", "CorregidaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_CorreccionesCategoria_CategoriaAnteriorId",
                table: "CorreccionesCategoria",
                column: "CategoriaAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_CorreccionesCategoria_CategoriaNuevaId",
                table: "CorreccionesCategoria",
                column: "CategoriaNuevaId");

            migrationBuilder.CreateIndex(
                name: "IX_CorreccionesCategoria_OperadorId",
                table: "CorreccionesCategoria",
                column: "OperadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorreccionesCategoria");
        }
    }
}
