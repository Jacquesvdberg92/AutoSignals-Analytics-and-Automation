using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class ChangesToProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Provider",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastProvidedSignal",
                table: "Provider",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Provider");

            migrationBuilder.DropColumn(
                name: "LastProvidedSignal",
                table: "Provider");
        }
    }
}
