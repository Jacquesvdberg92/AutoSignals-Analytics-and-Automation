using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class DynamicTpProbabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tp1Probability",
                table: "SignalPredictions");

            migrationBuilder.DropColumn(
                name: "Tp2Probability",
                table: "SignalPredictions");

            migrationBuilder.DropColumn(
                name: "Tp3Probability",
                table: "SignalPredictions");

            migrationBuilder.AddColumn<string>(
                name: "TpProbabilities",
                table: "SignalPredictions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TpProbabilities",
                table: "SignalPredictions");

            migrationBuilder.AddColumn<float>(
                name: "Tp1Probability",
                table: "SignalPredictions",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "Tp2Probability",
                table: "SignalPredictions",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.AddColumn<float>(
                name: "Tp3Probability",
                table: "SignalPredictions",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }
    }
}
