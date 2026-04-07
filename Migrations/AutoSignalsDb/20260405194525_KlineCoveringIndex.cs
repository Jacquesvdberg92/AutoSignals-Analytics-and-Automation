using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class KlineCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KLineAssetPrices_Symbol_Type_Time",
                table: "KLineAssetPrices");

            migrationBuilder.CreateIndex(
                name: "IX_KLineAssetPrices_Symbol_Type_Time",
                table: "KLineAssetPrices",
                columns: new[] { "Symbol", "Type", "Time" })
                .Annotation("SqlServer:Include", new[] { "Price", "Open", "High", "Low", "Close", "Volume" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KLineAssetPrices_Symbol_Type_Time",
                table: "KLineAssetPrices");

            migrationBuilder.CreateIndex(
                name: "IX_KLineAssetPrices_Symbol_Type_Time",
                table: "KLineAssetPrices",
                columns: new[] { "Symbol", "Type", "Time" });
        }
    }
}
