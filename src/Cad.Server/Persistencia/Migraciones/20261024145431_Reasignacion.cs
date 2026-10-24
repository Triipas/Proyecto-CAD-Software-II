using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Reasignacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reasignaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerenoAnteriorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerenoNuevoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReasignadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reasignaciones", x => x.Id);
                    table.CheckConstraint("CK_Reasignaciones_Motivo", "length(btrim(\"Motivo\")) > 0");
                    table.ForeignKey(
                        name: "FK_Reasignaciones_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reasignaciones_AspNetUsers_SerenoAnteriorId",
                        column: x => x.SerenoAnteriorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reasignaciones_AspNetUsers_SerenoNuevoId",
                        column: x => x.SerenoNuevoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reasignaciones_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reasignaciones_CasoId",
                table: "Reasignaciones",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_Reasignaciones_OperadorId",
                table: "Reasignaciones",
                column: "OperadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Reasignaciones_SerenoAnteriorId",
                table: "Reasignaciones",
                column: "SerenoAnteriorId");

            migrationBuilder.CreateIndex(
                name: "IX_Reasignaciones_SerenoNuevoId",
                table: "Reasignaciones",
                column: "SerenoNuevoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reasignaciones");
        }
    }
}
