using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class updates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientOrderId",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeOrderStatus",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeResponseJson",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderId",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncTime",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SignalPredictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SignalId = table.Column<int>(type: "int", nullable: false),
                    ConfidenceScore = table.Column<float>(type: "real", nullable: false),
                    Tp1Probability = table.Column<float>(type: "real", nullable: false),
                    Tp2Probability = table.Column<float>(type: "real", nullable: false),
                    Tp3Probability = table.Column<float>(type: "real", nullable: false),
                    StoplossProbability = table.Column<float>(type: "real", nullable: false),
                    ProviderAccuracyScore = table.Column<float>(type: "real", nullable: false),
                    MarketAlignmentScore = table.Column<float>(type: "real", nullable: false),
                    VolatilityFitScore = table.Column<float>(type: "real", nullable: false),
                    HistoricalSampleSize = table.Column<int>(type: "int", nullable: false),
                    ProviderSampleSize = table.Column<int>(type: "int", nullable: false),
                    FeatureSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModelVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalPredictions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignalPredictions_SignalId",
                table: "SignalPredictions",
                column: "SignalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignalPredictions");

            migrationBuilder.DropColumn(
                name: "ClientOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeOrderStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExchangeResponseJson",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastSyncTime",
                table: "Orders");
        }
    }
}
