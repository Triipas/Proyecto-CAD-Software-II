using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class EstadoSereno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EstadosSereno",
                columns: table => new
                {
                    SerenoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operativo = table.Column<bool>(type: "boolean", nullable: false),
                    CambiadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstadosSereno", x => x.SerenoId);
                    table.ForeignKey(
                        name: "FK_EstadosSereno_AspNetUsers_SerenoId",
                        column: x => x.SerenoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosicionesSereno",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerenoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitud = table.Column<double>(type: "double precision", nullable: false),
                    Longitud = table.Column<double>(type: "double precision", nullable: false),
                    PrecisionMetros = table.Column<double>(type: "double precision", nullable: false),
                    ObtenidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecibidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosicionesSereno", x => x.Id);
                    table.CheckConstraint("CK_PosicionesSereno_Latitud", "\"Latitud\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_PosicionesSereno_Longitud", "\"Longitud\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_PosicionesSereno_Precision", "\"PrecisionMetros\" > 0");
                    table.ForeignKey(
                        name: "FK_PosicionesSereno_AspNetUsers_SerenoId",
                        column: x => x.SerenoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosicionesSereno_SerenoId_ObtenidaEn",
                table: "PosicionesSereno",
                columns: new[] { "SerenoId", "ObtenidaEn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EstadosSereno");

            migrationBuilder.DropTable(
                name: "PosicionesSereno");
        }
    }
}
