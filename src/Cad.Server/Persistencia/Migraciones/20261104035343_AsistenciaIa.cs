using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AsistenciaIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AjustesIa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RetrasoSegundos = table.Column<int>(type: "integer", nullable: false),
                    MaximoPorHora = table.Column<int>(type: "integer", nullable: false),
                    LongitudMinimaDescripcion = table.Column<int>(type: "integer", nullable: false),
                    PermitirCorregirCategoria = table.Column<bool>(type: "boolean", nullable: false),
                    FrenoMinimoAdmisiones = table.Column<int>(type: "integer", nullable: false),
                    FrenoPorcentajeDescartes = table.Column<int>(type: "integer", nullable: false),
                    CambiadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AjustesIa", x => x.Id);
                    table.CheckConstraint("CK_AjustesIa_Unico", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "CambiosAjusteIa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModoAnterior = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ModoNuevo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Parametros = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    En = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CambiosAjusteIa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CambiosAjusteIa_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SugerenciasReporte",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionPrompt = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VersionCriterios = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Modelo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ModeloVerificador = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CategoriaSugeridaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoriaAlternativaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Confianza = table.Column<short>(type: "smallint", nullable: false),
                    Justificacion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Decision = table.Column<short>(type: "smallint", nullable: false),
                    MotivoBorrador = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DestinoCodigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AccionBorrador = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MotivoDescarteCodigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Prioridad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PosibleUrgencia = table.Column<bool>(type: "boolean", nullable: false),
                    MotivoUrgencia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Senales = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DuplicadoDeId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerificadorCoincide = table.Column<bool>(type: "boolean", nullable: true),
                    Escalada = table.Column<bool>(type: "boolean", nullable: false),
                    LatenciaMs = table.Column<int>(type: "integer", nullable: false),
                    TokensEntrada = table.Column<int>(type: "integer", nullable: false),
                    TokensSalida = table.Column<int>(type: "integer", nullable: false),
                    CreadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SugerenciasReporte", x => x.Id);
                    table.CheckConstraint("CK_SugerenciasReporte_Confianza", "\"Confianza\" BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_SugerenciasReporte_Decision", "\"Decision\" BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_SugerenciasReporte_Destino", "\"DestinoCodigo\" IS NULL OR \"DestinoCodigo\" IN ('Pnp', 'Fiscalia', 'Cem', 'Samu', 'Bomberos', 'Municipalidad', 'Otro')");
                    table.CheckConstraint("CK_SugerenciasReporte_MotivoDescarte", "\"MotivoDescarteCodigo\" IS NULL OR \"MotivoDescarteCodigo\" IN ('FalsoOMalintencionado', 'Duplicado', 'NoVerificable', 'FueraDeAlcance', 'ActivacionAccidental', 'Otro')");
                    table.CheckConstraint("CK_SugerenciasReporte_Prioridad", "\"Prioridad\" IS NULL OR \"Prioridad\" IN ('Alta', 'Media', 'Baja')");
                    table.ForeignKey(
                        name: "FK_SugerenciasReporte_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SugerenciasReporte_Casos_DuplicadoDeId",
                        column: x => x.DuplicadoDeId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SugerenciasReporte_CategoriasIncidencia_CategoriaAlternativ~",
                        column: x => x.CategoriaAlternativaId,
                        principalTable: "CategoriasIncidencia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SugerenciasReporte_CategoriasIncidencia_CategoriaSugeridaId",
                        column: x => x.CategoriaSugeridaId,
                        principalTable: "CategoriasIncidencia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AjustesIa",
                columns: new[] { "Id", "CambiadoEn", "FrenoMinimoAdmisiones", "FrenoPorcentajeDescartes", "LongitudMinimaDescripcion", "MaximoPorHora", "Modo", "PermitirCorregirCategoria", "RetrasoSegundos" },
                values: new object[] { 1, null, 20, 5, 40, 60, "Apagada", false, 30 });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "Activo", "Apellidos", "CodigoPersonal", "ConcurrencyStamp", "CreadoEn", "DebeCambiarContrasena", "Dni", "Email", "EmailConfirmed", "IntentosPinFallidos", "LockoutEnabled", "LockoutEnd", "Nombres", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "PinHash", "SecurityStamp", "TerminosAceptadosEn", "TerminosVersion", "TwoFactorEnabled", "UserName" },
                values: new object[] { new Guid("0199d6a0-0000-7000-8000-0000000000a1"), 0, false, "CAD (automático)", null, "0199d6a0-0000-7000-8000-0000000000a1", new DateTimeOffset(new DateTime(2026, 11, 3, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, null, null, false, 0, true, new DateTimeOffset(new DateTime(9999, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified).AddTicks(9999), new TimeSpan(0, 0, 0, 0, 0)), "Sistema", null, "SISTEMA.CAD", null, null, false, null, "0199d6a0-0000-7000-8000-0000000000a1", null, null, false, "sistema.cad" });

            migrationBuilder.CreateIndex(
                name: "IX_CambiosAjusteIa_ActorId",
                table: "CambiosAjusteIa",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_CambiosAjusteIa_En",
                table: "CambiosAjusteIa",
                column: "En");

            migrationBuilder.CreateIndex(
                name: "IX_SugerenciasReporte_CasoId_VersionPrompt",
                table: "SugerenciasReporte",
                columns: new[] { "CasoId", "VersionPrompt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SugerenciasReporte_CategoriaAlternativaId",
                table: "SugerenciasReporte",
                column: "CategoriaAlternativaId");

            migrationBuilder.CreateIndex(
                name: "IX_SugerenciasReporte_CategoriaSugeridaId",
                table: "SugerenciasReporte",
                column: "CategoriaSugeridaId");

            migrationBuilder.CreateIndex(
                name: "IX_SugerenciasReporte_DuplicadoDeId",
                table: "SugerenciasReporte",
                column: "DuplicadoDeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AjustesIa");

            migrationBuilder.DropTable(
                name: "CambiosAjusteIa");

            migrationBuilder.DropTable(
                name: "SugerenciasReporte");

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: new Guid("0199d6a0-0000-7000-8000-0000000000a1"));
        }
    }
}
