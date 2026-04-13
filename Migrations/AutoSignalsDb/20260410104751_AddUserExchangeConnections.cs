using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class AddUserExchangeConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "ProvidersSettings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserExchangeConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExchangeId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApiPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TestResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastTestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserExchangeConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserExchangeConnections_Exchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalTable: "Exchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProvidersSettings_ConnectionId",
                table: "ProvidersSettings",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserExchangeConnections_ExchangeId",
                table: "UserExchangeConnections",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserExchangeConnections_UserId",
                table: "UserExchangeConnections",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProvidersSettings_UserExchangeConnections_ConnectionId",
                table: "ProvidersSettings",
                column: "ConnectionId",
                principalTable: "UserExchangeConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Copy existing single-connection data from UsersData into the new table.
            // Only migrate rows where ExchangeId IS NOT NULL (user had a connection configured).
            migrationBuilder.Sql(@"
                INSERT INTO UserExchangeConnections
                    (UserId, ExchangeId, ApiKey, ApiSecret, ApiPassword, IsDefault, IsActive,
                     Label, TestResult, LastTestedAt, CreatedAt, UpdatedAt)
                SELECT
                    Id,
                    ExchangeId,
                    ApiKey,
                    ApiSecret,
                    ApiPassword,
                    1,
                    1,
                    'Primary Connection',
                    ApiTestResult,
                    CASE WHEN ApiTestResult IS NOT NULL THEN GETUTCDATE() ELSE NULL END,
                    GETUTCDATE(),
                    GETUTCDATE()
                FROM UsersData
                WHERE ExchangeId IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProvidersSettings_UserExchangeConnections_ConnectionId",
                table: "ProvidersSettings");

            migrationBuilder.DropTable(
                name: "UserExchangeConnections");

            migrationBuilder.DropIndex(
                name: "IX_ProvidersSettings_ConnectionId",
                table: "ProvidersSettings");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "ProvidersSettings");
        }
    }
}
