using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class Exchange_update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Exchanges",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Exchanges",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReferralBonus",
                table: "Exchanges",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Exchanges",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Exchanges");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Exchanges");

            migrationBuilder.DropColumn(
                name: "ReferralBonus",
                table: "Exchanges");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Exchanges");
        }
    }
}
