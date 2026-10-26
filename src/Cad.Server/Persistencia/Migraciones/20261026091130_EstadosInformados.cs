using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class EstadosInformados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EstadosInformados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SesionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CiudadanoId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdSolicitud = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InformadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstadosInformados", x => x.Id);
                    table.CheckConstraint("CK_EstadosInformados_Sos", "(\"Estado\" = 'NecesitoAyuda' AND \"CasoId\" IS NOT NULL) OR (\"Estado\" <> 'NecesitoAyuda' AND \"CasoId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_EstadosInformados_AspNetUsers_CiudadanoId",
                        column: x => x.CiudadanoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstadosInformados_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EstadosInformados_SesionesAcompanamiento_SesionId",
                        column: x => x.SesionId,
                        principalTable: "SesionesAcompanamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EstadosInformados_CasoId",
                table: "EstadosInformados",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_EstadosInformados_CiudadanoId_IdSolicitud",
                table: "EstadosInformados",
                columns: new[] { "CiudadanoId", "IdSolicitud" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EstadosInformados_SesionId_InformadoEn",
                table: "EstadosInformados",
                columns: new[] { "SesionId", "InformadoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EstadosInformados");
        }
    }
}
