using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddTypeToKLineAssetPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "KLineAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "KLineAssetPrices",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_KLineAssetPrices_Symbol_Type_Time",
                table: "KLineAssetPrices",
                columns: new[] { "Symbol", "Type", "Time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KLineAssetPrices_Symbol_Type_Time",
                table: "KLineAssetPrices");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "KLineAssetPrices");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "KLineAssetPrices",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
