using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddUserNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserNotificationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TelegramOrderExecuted = table.Column<bool>(type: "bit", nullable: false),
                    TelegramTakeProfitHit = table.Column<bool>(type: "bit", nullable: false),
                    TelegramStopLossHit = table.Column<bool>(type: "bit", nullable: false),
                    EmailOrderExecuted = table.Column<bool>(type: "bit", nullable: false),
                    EmailTakeProfitHit = table.Column<bool>(type: "bit", nullable: false),
                    EmailStopLossHit = table.Column<bool>(type: "bit", nullable: false),
                    EmailMarketing = table.Column<bool>(type: "bit", nullable: false),
                    EmailUpdates = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotificationSettings_UserId",
                table: "UserNotificationSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNotificationSettings");
        }
    }
}
