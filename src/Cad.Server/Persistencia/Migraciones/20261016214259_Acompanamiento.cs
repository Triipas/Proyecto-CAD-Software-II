using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Acompanamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SesionesAcompanamiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CiudadanoId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdSolicitud = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IniciadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VenceEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CerradaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SesionesAcompanamiento", x => x.Id);
                    table.CheckConstraint("CK_SesionesAcompanamiento_Cierre", "(\"Estado\" = 'Activa' AND \"CerradaEn\" IS NULL) OR (\"Estado\" IN ('Finalizada', 'Vencida') AND \"CerradaEn\" IS NOT NULL AND \"CerradaEn\" >= \"IniciadaEn\")");
                    table.CheckConstraint("CK_SesionesAcompanamiento_Vencimiento", "\"VenceEn\" > \"IniciadaEn\"");
                    table.ForeignKey(
                        name: "FK_SesionesAcompanamiento_AspNetUsers_CiudadanoId",
                        column: x => x.CiudadanoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosicionesAcompanamiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SesionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitud = table.Column<double>(type: "double precision", nullable: false),
                    Longitud = table.Column<double>(type: "double precision", nullable: false),
                    PrecisionMetros = table.Column<double>(type: "double precision", nullable: false),
                    ObtenidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecibidaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosicionesAcompanamiento", x => x.Id);
                    table.CheckConstraint("CK_PosicionesAcompanamiento_Latitud", "\"Latitud\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_PosicionesAcompanamiento_Longitud", "\"Longitud\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_PosicionesAcompanamiento_Precision", "\"PrecisionMetros\" > 0");
                    table.ForeignKey(
                        name: "FK_PosicionesAcompanamiento_SesionesAcompanamiento_SesionId",
                        column: x => x.SesionId,
                        principalTable: "SesionesAcompanamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosicionesAcompanamiento_SesionId_ObtenidaEn",
                table: "PosicionesAcompanamiento",
                columns: new[] { "SesionId", "ObtenidaEn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SesionesAcompanamiento_Activa",
                table: "SesionesAcompanamiento",
                column: "CiudadanoId",
                unique: true,
                filter: "\"Estado\" = 'Activa'");

            migrationBuilder.CreateIndex(
                name: "IX_SesionesAcompanamiento_CiudadanoId_IdSolicitud",
                table: "SesionesAcompanamiento",
                columns: new[] { "CiudadanoId", "IdSolicitud" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SesionesAcompanamiento_Estado_VenceEn",
                table: "SesionesAcompanamiento",
                columns: new[] { "Estado", "VenceEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosicionesAcompanamiento");

            migrationBuilder.DropTable(
                name: "SesionesAcompanamiento");
        }
    }
}
