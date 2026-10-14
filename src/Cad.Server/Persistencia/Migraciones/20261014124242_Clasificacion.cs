using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Clasificacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoriaId",
                table: "Casos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClasificadoEn",
                table: "Casos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClasificadoPor",
                table: "Casos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CategoriasIncidencia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriasIncidencia", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "CategoriasIncidencia",
                columns: new[] { "Id", "Nombre", "Orden" },
                values: new object[,]
                {
                    { new Guid("0199e2b0-0000-7000-8000-000000000001"), "Violencia doméstica", 1 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000002"), "Robo", 2 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000003"), "Presunta actividad relacionada con drogas", 3 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000004"), "Otros", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Casos_CategoriaId",
                table: "Casos",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_Casos_ClasificadoPor",
                table: "Casos",
                column: "ClasificadoPor");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Casos_ClasificacionCompleta",
                table: "Casos",
                sql: "(\"CategoriaId\" IS NULL AND \"ClasificadoPor\" IS NULL AND \"ClasificadoEn\" IS NULL) OR (\"CategoriaId\" IS NOT NULL AND \"ClasificadoPor\" IS NOT NULL AND \"ClasificadoEn\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasIncidencia_Nombre",
                table: "CategoriasIncidencia",
                column: "Nombre",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Casos_AspNetUsers_ClasificadoPor",
                table: "Casos",
                column: "ClasificadoPor",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Casos_CategoriasIncidencia_CategoriaId",
                table: "Casos",
                column: "CategoriaId",
                principalTable: "CategoriasIncidencia",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Casos_AspNetUsers_ClasificadoPor",
                table: "Casos");

            migrationBuilder.DropForeignKey(
                name: "FK_Casos_CategoriasIncidencia_CategoriaId",
                table: "Casos");

            migrationBuilder.DropTable(
                name: "CategoriasIncidencia");

            migrationBuilder.DropIndex(
                name: "IX_Casos_CategoriaId",
                table: "Casos");

            migrationBuilder.DropIndex(
                name: "IX_Casos_ClasificadoPor",
                table: "Casos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Casos_ClasificacionCompleta",
                table: "Casos");

            migrationBuilder.DropColumn(
                name: "CategoriaId",
                table: "Casos");

            migrationBuilder.DropColumn(
                name: "ClasificadoEn",
                table: "Casos");

            migrationBuilder.DropColumn(
                name: "ClasificadoPor",
                table: "Casos");
        }
    }
}
