using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AvisosAcompanamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvisosAcompanamiento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SesionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CiudadanoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Regla = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Clave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    GeneradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReferenciaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmacionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionIniciadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResueltoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    ResueltoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: true),
                    VinculadoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    VinculadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CasoCreado = table.Column<bool>(type: "boolean", nullable: false),
                    JustificacionCaso = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvisosAcompanamiento", x => x.Id);
                    table.CheckConstraint("CK_AvisosAcompanamiento_Caso", "(\"CasoId\" IS NULL AND \"VinculadoPor\" IS NULL AND \"CasoCreado\" = FALSE AND \"JustificacionCaso\" IS NULL) OR (\"CasoId\" IS NOT NULL AND \"VinculadoPor\" IS NOT NULL AND \"VinculadoEn\" IS NOT NULL AND (\"CasoCreado\" = FALSE OR (\"JustificacionCaso\" IS NOT NULL AND length(btrim(\"JustificacionCaso\")) > 0)))");
                    table.CheckConstraint("CK_AvisosAcompanamiento_Estado", "(\"Estado\" = 'Generado' AND \"ResponsableId\" IS NULL AND \"ResueltoEn\" IS NULL) OR (\"Estado\" = 'EnRevision' AND \"ResponsableId\" IS NOT NULL AND \"RevisionIniciadaEn\" IS NOT NULL AND \"ResueltoEn\" IS NULL) OR (\"Estado\" = 'Resuelto' AND \"ResponsableId\" IS NOT NULL AND \"ResueltoPor\" IS NOT NULL AND \"ResueltoEn\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AvisosAcompanamiento_AspNetUsers_CiudadanoId",
                        column: x => x.CiudadanoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvisosAcompanamiento_AspNetUsers_ResponsableId",
                        column: x => x.ResponsableId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvisosAcompanamiento_AspNetUsers_ResueltoPor",
                        column: x => x.ResueltoPor,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvisosAcompanamiento_AspNetUsers_VinculadoPor",
                        column: x => x.VinculadoPor,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvisosAcompanamiento_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvisosAcompanamiento_ConfirmacionesBienestar_ConfirmacionId",
                        column: x => x.ConfirmacionId,
                        principalTable: "ConfirmacionesBienestar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AvisosAcompanamiento_SesionesAcompanamiento_SesionId",
                        column: x => x.SesionId,
                        principalTable: "SesionesAcompanamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActuacionesAviso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AvisoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistradaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdSolicitud = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActuacionesAviso", x => x.Id);
                    table.CheckConstraint("CK_ActuacionesAviso_Descripcion", "\"Descripcion\" IS NOT NULL AND length(btrim(\"Descripcion\")) > 0");
                    table.ForeignKey(
                        name: "FK_ActuacionesAviso_AspNetUsers_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActuacionesAviso_AvisosAcompanamiento_AvisoId",
                        column: x => x.AvisoId,
                        principalTable: "AvisosAcompanamiento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActuacionesAviso_AvisoId_RegistradaEn",
                table: "ActuacionesAviso",
                columns: new[] { "AvisoId", "RegistradaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_ActuacionesAviso_OperadorId_IdSolicitud",
                table: "ActuacionesAviso",
                columns: new[] { "OperadorId", "IdSolicitud" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_CasoId",
                table: "AvisosAcompanamiento",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_CiudadanoId",
                table: "AvisosAcompanamiento",
                column: "CiudadanoId");

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_ConfirmacionId",
                table: "AvisosAcompanamiento",
                column: "ConfirmacionId");

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_Estado_GeneradoEn",
                table: "AvisosAcompanamiento",
                columns: new[] { "Estado", "GeneradoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_ResponsableId",
                table: "AvisosAcompanamiento",
                column: "ResponsableId");

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_ResueltoPor",
                table: "AvisosAcompanamiento",
                column: "ResueltoPor");

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_SesionId_Regla_Clave",
                table: "AvisosAcompanamiento",
                columns: new[] { "SesionId", "Regla", "Clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvisosAcompanamiento_VinculadoPor",
                table: "AvisosAcompanamiento",
                column: "VinculadoPor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActuacionesAviso");

            migrationBuilder.DropTable(
                name: "AvisosAcompanamiento");
        }
    }
}
