using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class FotosReporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FotosReporte",
                columns: table => new
                {
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Contenido = table.Column<byte[]>(type: "bytea", nullable: false),
                    RegistradaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosReporte", x => new { x.CasoId, x.Numero });
                    table.CheckConstraint("CK_FotosReporte_Contenido", "octet_length(\"Contenido\") BETWEEN 1 AND 1048576");
                    table.CheckConstraint("CK_FotosReporte_Numero", "\"Numero\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_FotosReporte_DetallesReporte_CasoId",
                        column: x => x.CasoId,
                        principalTable: "DetallesReporte",
                        principalColumn: "CasoId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FotosReporte");
        }
    }
}
