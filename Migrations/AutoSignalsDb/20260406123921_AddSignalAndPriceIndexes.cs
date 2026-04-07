using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddSignalAndPriceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "Signals",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "Signals",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Signals_Provider",
                table: "Signals",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_Signals_Symbol",
                table: "Signals",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_SignalPerformances_SignalId",
                table: "SignalPerformances",
                column: "SignalId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralAssetPrices_Symbol_Time",
                table: "GeneralAssetPrices",
                columns: new[] { "Symbol", "Time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Signals_Provider",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_Signals_Symbol",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_SignalPerformances_SignalId",
                table: "SignalPerformances");

            migrationBuilder.DropIndex(
                name: "IX_GeneralAssetPrices_Symbol_Time",
                table: "GeneralAssetPrices");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "Signals",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                table: "Signals",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
