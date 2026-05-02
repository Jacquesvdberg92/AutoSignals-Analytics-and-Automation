using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddArbitrageScannerColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlertCooldownMinutes",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoExecute",
                table: "Bots",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedFeePercent",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAlertAt",
                table: "Bots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxPositionSizeUsd",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinSpreadPercent",
                table: "Bots",
                type: "decimal(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalOpportunitiesFound",
                table: "Bots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WatchedSymbolsJson",
                table: "Bots",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArbitrageOpportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScannerId = table.Column<int>(type: "int", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BuyExchange = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SellExchange = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BuyPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    SellPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    SpreadPercent = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    NetSpreadPercent = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Alerted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArbitrageOpportunities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArbitrageOpportunities_Bots_ScannerId",
                        column: x => x.ScannerId,
                        principalTable: "Bots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArbitrageOpportunities_DetectedAt",
                table: "ArbitrageOpportunities",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ArbitrageOpportunities_ScannerId",
                table: "ArbitrageOpportunities",
                column: "ScannerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArbitrageOpportunities");

            migrationBuilder.DropColumn(
                name: "AlertCooldownMinutes",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "AutoExecute",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "EstimatedFeePercent",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "LastAlertAt",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "MaxPositionSizeUsd",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "MinSpreadPercent",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "TotalOpportunitiesFound",
                table: "Bots");

            migrationBuilder.DropColumn(
                name: "WatchedSymbolsJson",
                table: "Bots");
        }
    }
}
