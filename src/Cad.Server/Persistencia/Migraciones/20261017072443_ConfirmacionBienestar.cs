using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ConfirmacionBienestar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfirmacionesBienestar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SesionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CiudadanoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdSolicitud = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PresentadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Respuesta = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RespondidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfirmacionesBienestar", x => x.Id);
                    table.CheckConstraint("CK_ConfirmacionesBienestar_Respuesta", "(\"Respuesta\" IS NULL AND \"RespondidaEn\" IS NULL AND \"CasoId\" IS NULL) OR (\"Respuesta\" = 'EstoyBien' AND \"RespondidaEn\" IS NOT NULL AND \"CasoId\" IS NULL) OR (\"Respuesta\" = 'NecesitoAyuda' AND \"RespondidaEn\" IS NOT NULL AND \"CasoId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ConfirmacionesBienestar_AspNetUsers_CiudadanoId",
                        column: x => x.CiudadanoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmacionesBienestar_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmacionesBienestar_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfirmacionesBienestar_SesionesAcompanamiento_SesionId",
                        column: x => x.SesionId,
                        principalTable: "SesionesAcompanamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmacionesBienestar_CasoId",
                table: "ConfirmacionesBienestar",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmacionesBienestar_CiudadanoId",
                table: "ConfirmacionesBienestar",
                column: "CiudadanoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmacionesBienestar_OperadorId_IdSolicitud",
                table: "ConfirmacionesBienestar",
                columns: new[] { "OperadorId", "IdSolicitud" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfirmacionesBienestar_Pendiente",
                table: "ConfirmacionesBienestar",
                column: "SesionId",
                unique: true,
                filter: "\"Respuesta\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfirmacionesBienestar");
        }
    }
}
