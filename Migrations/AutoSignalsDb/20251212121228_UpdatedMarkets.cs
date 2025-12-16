using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class UpdatedMarkets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFutures",
                table: "OkxMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpot",
                table: "OkxMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "OkxMarkets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsFutures",
                table: "KuCoinMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpot",
                table: "KuCoinMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "KuCoinMarkets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsFutures",
                table: "BybitMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpot",
                table: "BybitMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BybitMarkets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsFutures",
                table: "BinanceMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpot",
                table: "BinanceMarkets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BinanceMarkets",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFutures",
                table: "OkxMarkets");

            migrationBuilder.DropColumn(
                name: "IsSpot",
                table: "OkxMarkets");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "OkxMarkets");

            migrationBuilder.DropColumn(
                name: "IsFutures",
                table: "KuCoinMarkets");

            migrationBuilder.DropColumn(
                name: "IsSpot",
                table: "KuCoinMarkets");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "KuCoinMarkets");

            migrationBuilder.DropColumn(
                name: "IsFutures",
                table: "BybitMarkets");

            migrationBuilder.DropColumn(
                name: "IsSpot",
                table: "BybitMarkets");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BybitMarkets");

            migrationBuilder.DropColumn(
                name: "IsFutures",
                table: "BinanceMarkets");

            migrationBuilder.DropColumn(
                name: "IsSpot",
                table: "BinanceMarkets");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BinanceMarkets");
        }
    }
}
