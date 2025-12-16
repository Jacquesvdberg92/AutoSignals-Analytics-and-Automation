using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class addedType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OkxAssetPrices_Symbol",
                table: "OkxAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_KuCoinAssetPrices_Symbol",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_GeneralAssetPrices_Symbol",
                table: "GeneralAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_BybitAssetPrices_Symbol",
                table: "BybitAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_BitgetAssetPrices_Symbol",
                table: "BitgetAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_BinanceAssetPrices_Symbol",
                table: "BinanceAssetPrices");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "OkxAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "KuCoinAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "GeneralAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BybitAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "BitgetMarkets",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "BitgetMarkets",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BitgetAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BinanceAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_OkxAssetPrices_Symbol_Type",
                table: "OkxAssetPrices",
                columns: new[] { "Symbol", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KuCoinAssetPrices_Symbol_Type",
                table: "KuCoinAssetPrices",
                columns: new[] { "Symbol", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralAssetPrices_Symbol_Type",
                table: "GeneralAssetPrices",
                columns: new[] { "Symbol", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BybitAssetPrices_Symbol_Type",
                table: "BybitAssetPrices",
                columns: new[] { "Symbol", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BitgetMarkets_Symbol_Type",
                table: "BitgetMarkets",
                columns: new[] { "Symbol", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BitgetAssetPrices_Symbol_Type",
                table: "BitgetAssetPrices",
                columns: new[] { "Symbol", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BinanceAssetPrices_Symbol_Type",
                table: "BinanceAssetPrices",
                columns: new[] { "Symbol", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OkxAssetPrices_Symbol_Type",
                table: "OkxAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_KuCoinAssetPrices_Symbol_Type",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_GeneralAssetPrices_Symbol_Type",
                table: "GeneralAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_BybitAssetPrices_Symbol_Type",
                table: "BybitAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_BitgetMarkets_Symbol_Type",
                table: "BitgetMarkets");

            migrationBuilder.DropIndex(
                name: "IX_BitgetAssetPrices_Symbol_Type",
                table: "BitgetAssetPrices");

            migrationBuilder.DropIndex(
                name: "IX_BinanceAssetPrices_Symbol_Type",
                table: "BinanceAssetPrices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "OkxAssetPrices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "KuCoinAssetPrices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "GeneralAssetPrices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BybitAssetPrices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BitgetAssetPrices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BinanceAssetPrices");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "BitgetMarkets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "BitgetMarkets",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_OkxAssetPrices_Symbol",
                table: "OkxAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KuCoinAssetPrices_Symbol",
                table: "KuCoinAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralAssetPrices_Symbol",
                table: "GeneralAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BybitAssetPrices_Symbol",
                table: "BybitAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BitgetAssetPrices_Symbol",
                table: "BitgetAssetPrices",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BinanceAssetPrices_Symbol",
                table: "BinanceAssetPrices",
                column: "Symbol",
                unique: true);
        }
    }
}
