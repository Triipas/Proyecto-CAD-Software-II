using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Cad.Server.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CatalogoCategoriasAmpliado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ayuda",
                table: "CategoriasIncidencia",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Icono",
                table: "CategoriasIncidencia",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000001"),
                columns: new[] { "Ayuda", "Icono", "Nombre", "Orden" },
                values: new object[] { "Agresión, gritos o maltrato en una vivienda", "violencia", "Violencia familiar", 2 });

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000002"),
                columns: new[] { "Ayuda", "Icono", "Nombre", "Orden" },
                values: new object[] { "Me robaron, intento de robo", "robo", "Robo o hurto", 1 });

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000003"),
                columns: new[] { "Ayuda", "Icono", "Nombre", "Orden" },
                values: new object[] { "Venta o consumo en un punto fijo", "drogas", "Consumo o venta de drogas", 5 });

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000004"),
                columns: new[] { "Ayuda", "Icono", "Orden" },
                values: new object[] { "Solo si nada de lo anterior encaja", "otros", 13 });

            migrationBuilder.InsertData(
                table: "CategoriasIncidencia",
                columns: new[] { "Id", "Ayuda", "Icono", "Nombre", "Orden" },
                values: new object[,]
                {
                    { new Guid("0199e2b0-0000-7000-8000-000000000005"), "Acoso, tocamientos o persecución", "acoso", "Acoso en la vía pública", 3 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000006"), "Cobro de cupos, amenazas", "extorsion", "Extorsión o amenazas", 4 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000007"), "Grupos bebiendo en la calle o el parque", "alcohol", "Consumo de alcohol en la vía pública", 6 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000008"), "Pelea entre personas o grupos", "pelea", "Peleas o riñas", 7 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000009"), "Merodeo, vehículo abandonado", "sospechoso", "Persona o vehículo sospechoso", 8 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000010"), "Pintas, destrozos, robo de mobiliario público", "vandalismo", "Daños o vandalismo", 9 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000011"), "Choque sin heridos graves", "accidente", "Accidente de tránsito", 10 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000012"), "Fiestas o escándalo en la vía pública", "ruido", "Ruidos molestos o escándalo", 11 },
                    { new Guid("0199e2b0-0000-7000-8000-000000000013"), "Menor o adulto mayor perdido, persona desorientada", "riesgo", "Persona en riesgo", 12 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000005"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000006"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000007"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000008"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000009"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000010"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000011"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000012"));

            migrationBuilder.DeleteData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000013"));

            migrationBuilder.DropColumn(
                name: "Ayuda",
                table: "CategoriasIncidencia");

            migrationBuilder.DropColumn(
                name: "Icono",
                table: "CategoriasIncidencia");

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000001"),
                columns: new[] { "Nombre", "Orden" },
                values: new object[] { "Violencia doméstica", 1 });

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000002"),
                columns: new[] { "Nombre", "Orden" },
                values: new object[] { "Robo", 2 });

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000003"),
                columns: new[] { "Nombre", "Orden" },
                values: new object[] { "Presunta actividad relacionada con drogas", 3 });

            migrationBuilder.UpdateData(
                table: "CategoriasIncidencia",
                keyColumn: "Id",
                keyValue: new Guid("0199e2b0-0000-7000-8000-000000000004"),
                column: "Orden",
                value: 4);
        }
    }
}
