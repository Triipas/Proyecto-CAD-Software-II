using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicioAtencionPorCentral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegistradoPorOperadorId",
                table: "IniciosAtencion",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IniciosAtencion_RegistradoPorOperadorId",
                table: "IniciosAtencion",
                column: "RegistradoPorOperadorId");

            migrationBuilder.AddForeignKey(
                name: "FK_IniciosAtencion_AspNetUsers_RegistradoPorOperadorId",
                table: "IniciosAtencion",
                column: "RegistradoPorOperadorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IniciosAtencion_AspNetUsers_RegistradoPorOperadorId",
                table: "IniciosAtencion");

            migrationBuilder.DropIndex(
                name: "IX_IniciosAtencion_RegistradoPorOperadorId",
                table: "IniciosAtencion");

            migrationBuilder.DropColumn(
                name: "RegistradoPorOperadorId",
                table: "IniciosAtencion");
        }
    }
}
