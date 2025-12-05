using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class prep_for_candles_update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Volume",
                table: "BitgetAssetPrices",
                type: "decimal(28,8)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Volume",
                table: "BitgetAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(28,8)");
        }
    }
}
