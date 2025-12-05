using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class priceUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Close",
                table: "OkxAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "High",
                table: "OkxAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Low",
                table: "OkxAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Open",
                table: "OkxAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Volume",
                table: "OkxAssetPrices",
                type: "decimal(28,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Close",
                table: "KuCoinAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "High",
                table: "KuCoinAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Low",
                table: "KuCoinAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Open",
                table: "KuCoinAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Volume",
                table: "KuCoinAssetPrices",
                type: "decimal(28,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Close",
                table: "GeneralAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "High",
                table: "GeneralAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Low",
                table: "GeneralAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Open",
                table: "GeneralAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Volume",
                table: "GeneralAssetPrices",
                type: "decimal(28,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Close",
                table: "BybitAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "High",
                table: "BybitAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Low",
                table: "BybitAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Open",
                table: "BybitAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Volume",
                table: "BybitAssetPrices",
                type: "decimal(28,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Close",
                table: "BinanceAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "High",
                table: "BinanceAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Low",
                table: "BinanceAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Open",
                table: "BinanceAssetPrices",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Volume",
                table: "BinanceAssetPrices",
                type: "decimal(28,8)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Close",
                table: "OkxAssetPrices");

            migrationBuilder.DropColumn(
                name: "High",
                table: "OkxAssetPrices");

            migrationBuilder.DropColumn(
                name: "Low",
                table: "OkxAssetPrices");

            migrationBuilder.DropColumn(
                name: "Open",
                table: "OkxAssetPrices");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "OkxAssetPrices");

            migrationBuilder.DropColumn(
                name: "Close",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropColumn(
                name: "High",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropColumn(
                name: "Low",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropColumn(
                name: "Open",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropColumn(
                name: "Close",
                table: "GeneralAssetPrices");

            migrationBuilder.DropColumn(
                name: "High",
                table: "GeneralAssetPrices");

            migrationBuilder.DropColumn(
                name: "Low",
                table: "GeneralAssetPrices");

            migrationBuilder.DropColumn(
                name: "Open",
                table: "GeneralAssetPrices");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "GeneralAssetPrices");

            migrationBuilder.DropColumn(
                name: "Close",
                table: "BybitAssetPrices");

            migrationBuilder.DropColumn(
                name: "High",
                table: "BybitAssetPrices");

            migrationBuilder.DropColumn(
                name: "Low",
                table: "BybitAssetPrices");

            migrationBuilder.DropColumn(
                name: "Open",
                table: "BybitAssetPrices");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "BybitAssetPrices");

            migrationBuilder.DropColumn(
                name: "Close",
                table: "BinanceAssetPrices");

            migrationBuilder.DropColumn(
                name: "High",
                table: "BinanceAssetPrices");

            migrationBuilder.DropColumn(
                name: "Low",
                table: "BinanceAssetPrices");

            migrationBuilder.DropColumn(
                name: "Open",
                table: "BinanceAssetPrices");

            migrationBuilder.DropColumn(
                name: "Volume",
                table: "BinanceAssetPrices");
        }
    }
}
