using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class prep_for_candles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Positions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<decimal>(
                name: "Close",
                table: "BitgetAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "High",
                table: "BitgetAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Low",
                table: "BitgetAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Open",
                table: "BitgetAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Volume",
                table: "BitgetAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Close",
                table: "BitgetAssetPrices");

            migrationBuilder.DropColumn(
                name: "High",
                table: "BitgetAssetPrices");

            migrationBuilder.DropColumn(
                name: "Low",
                table: "BitgetAssetPrices");

            migrationBuilder.DropColumn(
                name: "Open",
                table: "BitgetAssetPrices");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "BitgetAssetPrices");
        }
    }
}
