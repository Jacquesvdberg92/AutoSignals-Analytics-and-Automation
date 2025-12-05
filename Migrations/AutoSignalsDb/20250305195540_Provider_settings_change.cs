using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class Provider_settings_change : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IgnorLong",
                table: "ProvidersSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IgnorShort",
                table: "ProvidersSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreStoploss",
                table: "ProvidersSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Testing",
                table: "ProvidersSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "Positions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IgnorLong",
                table: "ProvidersSettings");

            migrationBuilder.DropColumn(
                name: "IgnorShort",
                table: "ProvidersSettings");

            migrationBuilder.DropColumn(
                name: "IgnoreStoploss",
                table: "ProvidersSettings");

            migrationBuilder.DropColumn(
                name: "Testing",
                table: "ProvidersSettings");

            migrationBuilder.DropColumn(
                name: "IsTest",
                table: "Positions");
        }
    }
}
