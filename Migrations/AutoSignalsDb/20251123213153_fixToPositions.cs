using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class fixToPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsIsolated",
                table: "Positions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsIsolated",
                table: "Positions");
        }
    }
}
