using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoSignals.Migrations.AutoSignalsDb
{
    /// <inheritdoc />
    public partial class NOWPaymentsV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LemonSqueezyCustomerId",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "UsersData");

            migrationBuilder.DropColumn(
                name: "GooglePlayProductId",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "LemonSqueezyVariantId",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "StripePriceId",
                table: "SubscriptionPlans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LemonSqueezyCustomerId",
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

            migrationBuilder.AddColumn<string>(
                name: "GooglePlayProductId",
                table: "SubscriptionPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LemonSqueezyVariantId",
                table: "SubscriptionPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripePriceId",
                table: "SubscriptionPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "GooglePlayProductId", "LemonSqueezyVariantId", "StripePriceId" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "GooglePlayProductId", "LemonSqueezyVariantId", "StripePriceId" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "GooglePlayProductId", "LemonSqueezyVariantId", "StripePriceId" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "GooglePlayProductId", "LemonSqueezyVariantId", "StripePriceId" },
                values: new object[] { null, null, null });
        }
    }
}
