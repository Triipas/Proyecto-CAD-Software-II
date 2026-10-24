using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ReporteDeLaCentral : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactoNombre",
                table: "DetallesReporte",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactoTelefono",
                table: "DetallesReporte",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RegistradoPorOperadorId",
                table: "DetallesReporte",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CiudadanoId",
                table: "Casos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_DetallesReporte_RegistradoPorOperadorId",
                table: "DetallesReporte",
                column: "RegistradoPorOperadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Casos_IdSolicitud_DeLaCentral",
                table: "Casos",
                column: "IdSolicitud",
                unique: true,
                filter: "\"CiudadanoId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Casos_CiudadanoSalvoReporte",
                table: "Casos",
                sql: "\"CiudadanoId\" IS NOT NULL OR \"Origen\" = 'Reporte'");

            migrationBuilder.AddForeignKey(
                name: "FK_DetallesReporte_AspNetUsers_RegistradoPorOperadorId",
                table: "DetallesReporte",
                column: "RegistradoPorOperadorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DetallesReporte_AspNetUsers_RegistradoPorOperadorId",
                table: "DetallesReporte");

            migrationBuilder.DropIndex(
                name: "IX_DetallesReporte_RegistradoPorOperadorId",
                table: "DetallesReporte");

            migrationBuilder.DropIndex(
                name: "IX_Casos_IdSolicitud_DeLaCentral",
                table: "Casos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Casos_CiudadanoSalvoReporte",
                table: "Casos");

            migrationBuilder.DropColumn(
                name: "ContactoNombre",
                table: "DetallesReporte");

            migrationBuilder.DropColumn(
                name: "ContactoTelefono",
                table: "DetallesReporte");

            migrationBuilder.DropColumn(
                name: "RegistradoPorOperadorId",
                table: "DetallesReporte");

            migrationBuilder.AlterColumn<Guid>(
                name: "CiudadanoId",
                table: "Casos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
