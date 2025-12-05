using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class TgMsgId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelegramMessageId",
                table: "SignalPerformances",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TelegramMessageId",
                table: "SignalPerformances");
        }
    }
}
