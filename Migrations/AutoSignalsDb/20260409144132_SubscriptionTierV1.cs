using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class SubscriptionTierV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UsersData_SubscriptionActive",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "SubscriptionActive",
                table: "UsersData");

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubscriptionId",
                table: "UsersData",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "UsersData",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "UsersData",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionEndDate",
                table: "UsersData",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionProvider",
                table: "UsersData",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionStartDate",
                table: "UsersData",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionStatus",
                table: "UsersData",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionTier",
                table: "UsersData",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndDate",
                table: "UsersData",
                type: "datetime2",
                nullable: true);

            // Default all pre-existing users to Freemium/Expired (they never had a structured trial)
            migrationBuilder.Sql(@"
                UPDATE UsersData SET
                    SubscriptionTier = 0,
                    SubscriptionStatus = 4,
                    SubscriptionProvider = 'Manual'
                WHERE SubscriptionTier = 0 AND SubscriptionStatus = 0;
            ");

            migrationBuilder.CreateTable(
                name: "SubscriptionEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExternalEventId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ExternalSubscriptionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    StripePriceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GooglePlayProductId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MonthlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsAnnual = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "Id", "Currency", "FeaturesJson", "GooglePlayProductId", "IsActive", "IsAnnual", "MonthlyPrice", "Name", "StripePriceId", "Tier" },
                values: new object[,]
                {
                    { 1, "USD", null, null, true, false, 29.00m, "Pro Monthly", null, 1 },
                    { 2, "USD", null, null, true, true, 23.00m, "Pro Annual", null, 1 },
                    { 3, "USD", null, null, true, false, 79.00m, "VIP Monthly", null, 2 },
                    { 4, "USD", null, null, true, true, 63.00m, "VIP Annual", null, 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsersData_SubscriptionStatus",
                table: "UsersData",
                column: "SubscriptionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_UsersData_SubscriptionTier",
                table: "UsersData",
                column: "SubscriptionTier");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEvents_ExternalEventId",
                table: "SubscriptionEvents",
                column: "ExternalEventId",
                unique: true,
                filter: "[ExternalEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEvents_UserId",
                table: "SubscriptionEvents",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionEvents");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropIndex(
                name: "IX_UsersData_SubscriptionStatus",
                table: "UsersData");

            migrationBuilder.DropIndex(
                name: "IX_UsersData_SubscriptionTier",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "ExternalSubscriptionId",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "SubscriptionEndDate",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "SubscriptionProvider",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "SubscriptionStartDate",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "SubscriptionTier",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "TrialEndDate",
                table: "UsersData");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionActive",
                table: "UsersData",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsersData_SubscriptionActive",
                table: "UsersData",
                column: "SubscriptionActive");
        }
    }
}
