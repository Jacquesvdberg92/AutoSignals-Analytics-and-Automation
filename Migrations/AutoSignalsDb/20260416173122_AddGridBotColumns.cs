using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddGridBotColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilledOrderCount",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GridBot_IsIsolated",
                table: "Bots",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GridBot_Leverage",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GridBot_TotalInvested",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GridCount",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GridInitialised",
                table: "Bots",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GridMode",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LowerPrice",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderSizeUsd",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StopOnLowerBreakout",
                table: "Bots",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StopOnUpperBreakout",
                table: "Bots",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalProfit",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UpperPrice",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilledOrderCount",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "GridBot_IsIsolated",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "GridBot_Leverage",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "GridBot_TotalInvested",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "GridCount",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "GridInitialised",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "GridMode",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "LowerPrice",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "OrderSizeUsd",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "StopOnLowerBreakout",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "StopOnUpperBreakout",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "TotalProfit",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "UpperPrice",
                table: "Bots");
        }
    }
}
