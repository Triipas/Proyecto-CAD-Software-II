using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AnaliticaSalud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AbiertoEnCentralEn",
                table: "Casos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PresentadoEnCentralEn",
                table: "Casos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EnviosPush",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerenoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnviadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnviosPush", x => x.Id);
                    table.CheckConstraint("CK_EnviosPush_Resultado", "\"Resultado\" IN ('Enviada', 'TokenInvalido', 'NoEnviada', 'SinDispositivo')");
                    table.ForeignKey(
                        name: "FK_EnviosPush_AspNetUsers_SerenoId",
                        column: x => x.SerenoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnviosPush_EnviadoEn",
                table: "EnviosPush",
                column: "EnviadoEn");

            migrationBuilder.CreateIndex(
                name: "IX_EnviosPush_SerenoId",
                table: "EnviosPush",
                column: "SerenoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnviosPush");

            migrationBuilder.DropColumn(
                name: "AbiertoEnCentralEn",
                table: "Casos");

            migrationBuilder.DropColumn(
                name: "PresentadoEnCentralEn",
                table: "Casos");
        }
    }
}
