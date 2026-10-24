using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class InicioAtencion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IniciosAtencion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerenoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitud = table.Column<double>(type: "double precision", nullable: true),
                    Longitud = table.Column<double>(type: "double precision", nullable: true),
                    PrecisionMetros = table.Column<double>(type: "double precision", nullable: true),
                    DistanciaMetros = table.Column<double>(type: "double precision", nullable: true),
                    RadioMetros = table.Column<double>(type: "double precision", nullable: true),
                    DentroDelRango = table.Column<bool>(type: "boolean", nullable: true),
                    Motivo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Detalle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IniciadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IniciosAtencion", x => x.Id);
                    table.CheckConstraint("CK_IniciosAtencion_MotivoFueraDeRango", "\"DentroDelRango\" IS DISTINCT FROM false OR \"Motivo\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_IniciosAtencion_AspNetUsers_SerenoId",
                        column: x => x.SerenoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IniciosAtencion_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IniciosAtencion_CasoId",
                table: "IniciosAtencion",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_IniciosAtencion_SerenoId",
                table: "IniciosAtencion",
                column: "SerenoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IniciosAtencion");
        }
    }
}
