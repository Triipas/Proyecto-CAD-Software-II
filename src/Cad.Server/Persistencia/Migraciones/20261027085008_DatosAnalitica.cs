using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class DatosAnalitica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MotivoCodigo",
                table: "Reasignaciones",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Canal",
                table: "DetallesReporte",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoCodigo",
                table: "DescartesCaso",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinoCodigo",
                table: "DecisionesReporte",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoCodigo",
                table: "DecisionesReporte",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultadoAtencion",
                table: "CierresCaso",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Heridos",
                table: "Casos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prioridad",
                table: "Casos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoCodigo",
                table: "CancelacionesSos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CambiosEstadoSereno",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerenoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operativo = table.Column<bool>(type: "boolean", nullable: false),
                    CambiadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CambiosEstadoSereno", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CambiosEstadoSereno_AspNetUsers_SerenoId",
                        column: x => x.SerenoId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reasignaciones_MotivoCodigo",
                table: "Reasignaciones",
                sql: "\"MotivoCodigo\" IS NULL OR \"MotivoCodigo\" IN ('SerenoNoResponde', 'SerenoOcupado', 'OtroMasCercano', 'FallaDelEquipo', 'Otro')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DetallesReporte_Canal",
                table: "DetallesReporte",
                sql: "\"Canal\" IS NULL OR \"Canal\" IN ('App', 'Llamada', 'Radio', 'Presencial', 'RedesSociales', 'Otro')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DescartesCaso_MotivoCodigo",
                table: "DescartesCaso",
                sql: "\"MotivoCodigo\" IS NULL OR \"MotivoCodigo\" IN ('FalsoOMalintencionado', 'Duplicado', 'NoVerificable', 'FueraDeAlcance', 'ActivacionAccidental', 'Otro')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DecisionesReporte_DestinoCodigo",
                table: "DecisionesReporte",
                sql: "\"DestinoCodigo\" IS NULL OR \"DestinoCodigo\" IN ('Pnp', 'Fiscalia', 'Cem', 'Samu', 'Bomberos', 'Municipalidad', 'Otro')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DecisionesReporte_MotivoCodigo",
                table: "DecisionesReporte",
                sql: "\"MotivoCodigo\" IS NULL OR \"MotivoCodigo\" IN ('FalsoOMalintencionado', 'Duplicado', 'NoVerificable', 'FueraDeAlcance', 'ActivacionAccidental', 'Otro')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CierresCaso_ResultadoAtencion",
                table: "CierresCaso",
                sql: "\"ResultadoAtencion\" IS NULL OR \"ResultadoAtencion\" IN ('ResueltoEnElLugar', 'EntregadoAPnp', 'Derivado', 'SinHallazgo', 'FalsaAlarma')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Casos_Heridos",
                table: "Casos",
                sql: "\"Heridos\" IS NULL OR \"Heridos\" IN ('Si', 'No', 'NoSeSabe')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Casos_Prioridad",
                table: "Casos",
                sql: "\"Prioridad\" IS NULL OR \"Prioridad\" IN ('Alta', 'Media', 'Baja')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CancelacionesSos_MotivoCodigo",
                table: "CancelacionesSos",
                sql: "\"MotivoCodigo\" IS NULL OR \"MotivoCodigo\" IN ('PorError', 'YaNoLoNecesito', 'MeAyudaronOtros', 'Otro')");

            migrationBuilder.CreateIndex(
                name: "IX_CambiosEstadoSereno_CambiadoEn",
                table: "CambiosEstadoSereno",
                column: "CambiadoEn");

            migrationBuilder.CreateIndex(
                name: "IX_CambiosEstadoSereno_SerenoId_CambiadoEn",
                table: "CambiosEstadoSereno",
                columns: new[] { "SerenoId", "CambiadoEn" });

            // Lo que ya se sabe de los datos anteriores: el estado vigente de cada sereno abre su historial, y un reporte que no
            // registró la central llegó por la app. Lo demás (resultados, motivos, prioridades) queda nulo: «sin registrar».
            migrationBuilder.Sql("""
                INSERT INTO "CambiosEstadoSereno" ("Id", "SerenoId", "Operativo", "CambiadoEn")
                SELECT gen_random_uuid(), "SerenoId", "Operativo", "CambiadoEn" FROM "EstadosSereno";
                UPDATE "DetallesReporte" SET "Canal" = 'App' WHERE "RegistradoPorOperadorId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CambiosEstadoSereno");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reasignaciones_MotivoCodigo",
                table: "Reasignaciones");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DetallesReporte_Canal",
                table: "DetallesReporte");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DescartesCaso_MotivoCodigo",
                table: "DescartesCaso");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DecisionesReporte_DestinoCodigo",
                table: "DecisionesReporte");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DecisionesReporte_MotivoCodigo",
                table: "DecisionesReporte");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CierresCaso_ResultadoAtencion",
                table: "CierresCaso");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Casos_Heridos",
                table: "Casos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Casos_Prioridad",
                table: "Casos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CancelacionesSos_MotivoCodigo",
                table: "CancelacionesSos");

            migrationBuilder.DropColumn(
                name: "MotivoCodigo",
                table: "Reasignaciones");

            migrationBuilder.DropColumn(
                name: "Canal",
                table: "DetallesReporte");

            migrationBuilder.DropColumn(
                name: "MotivoCodigo",
                table: "DescartesCaso");

            migrationBuilder.DropColumn(
                name: "DestinoCodigo",
                table: "DecisionesReporte");

            migrationBuilder.DropColumn(
                name: "MotivoCodigo",
                table: "DecisionesReporte");

            migrationBuilder.DropColumn(
                name: "ResultadoAtencion",
                table: "CierresCaso");

            migrationBuilder.DropColumn(
                name: "Heridos",
                table: "Casos");

            migrationBuilder.DropColumn(
                name: "Prioridad",
                table: "Casos");

            migrationBuilder.DropColumn(
                name: "MotivoCodigo",
                table: "CancelacionesSos");
        }
    }
}
