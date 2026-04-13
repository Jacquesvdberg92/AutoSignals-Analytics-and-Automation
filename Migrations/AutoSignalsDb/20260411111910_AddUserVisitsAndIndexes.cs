using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddUserVisitsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Signals_Provider",
                table: "Signals");

            migrationBuilder.CreateTable(
                name: "UserVisits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PagePath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BytesSent = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVisits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Signals_Provider_Time",
                table: "Signals",
                columns: new[] { "Provider", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_Signals_Time",
                table: "Signals",
                column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_SignalPerformances_StartTime",
                table: "SignalPerformances",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_UserVisits_IpAddress",
                table: "UserVisits",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_UserVisits_Timestamp",
                table: "UserVisits",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserVisits");

            migrationBuilder.DropIndex(
                name: "IX_Signals_Provider_Time",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_Signals_Time",
                table: "Signals");

            migrationBuilder.DropIndex(
                name: "IX_SignalPerformances_StartTime",
                table: "SignalPerformances");

            migrationBuilder.CreateIndex(
                name: "IX_Signals_Provider",
                table: "Signals",
                column: "Provider");
        }
    }
}
