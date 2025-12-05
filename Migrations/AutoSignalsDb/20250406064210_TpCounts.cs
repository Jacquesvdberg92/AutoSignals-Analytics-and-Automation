using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class TpCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TpCount",
                table: "ProvidersSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TpPercentages",
                table: "ProvidersSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TpCount",
                table: "ProvidersSettings");

            migrationBuilder.DropColumn(
                name: "TpPercentages",
                table: "ProvidersSettings");
        }
    }
}
