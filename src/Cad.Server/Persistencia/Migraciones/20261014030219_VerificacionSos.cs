using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class VerificacionSos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntentosContacto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdSolicitud = table.Column<Guid>(type: "uuid", nullable: false),
                    Resultado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InformacionHecho = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReferenciaUbicacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntentosContacto", x => x.Id);
                    table.CheckConstraint("CK_IntentosContacto_Resultado", "\"Resultado\" IN ('Contactado', 'SinRespuesta', 'NoFuePosibleComunicar')");
                    table.ForeignKey(
                        name: "FK_IntentosContacto_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntentosContacto_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntentosContacto_CasoId_IdSolicitud",
                table: "IntentosContacto",
                columns: new[] { "CasoId", "IdSolicitud" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntentosContacto_CasoId_RegistradoEn",
                table: "IntentosContacto",
                columns: new[] { "CasoId", "RegistradoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_IntentosContacto_OperadorId",
                table: "IntentosContacto",
                column: "OperadorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntentosContacto");
        }
    }
}
