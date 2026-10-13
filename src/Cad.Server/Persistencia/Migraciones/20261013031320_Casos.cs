using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Casos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Casos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CiudadanoId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdSolicitud = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Latitud = table.Column<double>(type: "double precision", nullable: true),
                    Longitud = table.Column<double>(type: "double precision", nullable: true),
                    PrecisionMetros = table.Column<double>(type: "double precision", nullable: true),
                    UbicacionObtenidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Casos", x => x.Id);
                    table.CheckConstraint("CK_Casos_Latitud", "\"Latitud\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_Casos_Longitud", "\"Longitud\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_Casos_Precision", "\"PrecisionMetros\" > 0");
                    table.CheckConstraint("CK_Casos_UbicacionCompleta", "(\"Latitud\" IS NULL AND \"Longitud\" IS NULL AND \"PrecisionMetros\" IS NULL AND \"UbicacionObtenidaEn\" IS NULL) OR (\"Latitud\" IS NOT NULL AND \"Longitud\" IS NOT NULL AND \"PrecisionMetros\" IS NOT NULL AND \"UbicacionObtenidaEn\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Casos_AspNetUsers_CiudadanoId",
                        column: x => x.CiudadanoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventosCaso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Visibilidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosCaso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosCaso_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EventosCaso_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Casos_CiudadanoId_IdSolicitud",
                table: "Casos",
                columns: new[] { "CiudadanoId", "IdSolicitud" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosCaso_ActorId",
                table: "EventosCaso",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosCaso_CasoId_OcurridoEn",
                table: "EventosCaso",
                columns: new[] { "CasoId", "OcurridoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosCaso");

            migrationBuilder.DropTable(
                name: "Casos");
        }
    }
}
